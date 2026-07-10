using System.Text.Json;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Platform;
using ServerCenter.Core.Updates;

namespace ServerCenter.Agent.Jobs;

// Executes an "update.apply" job: it composes the resolved policy (carried in the job params) with
// the pluggable "what" provider and the job spine. Flow: validate provider + preflight availability
// -> run preflight in order -> (optionally) stop the service -> provider.ApplyAsync -> restart the
// service -> record the reboot decision. The actual reboot is NOT performed here: rebooting mid-job
// would kill the agent before the terminal result lands, and a host reboot is its own special-policy
// job (brief 3.4). update.apply reports what the reboot policy decided; a follow-on job acts on it.
public sealed class UpdateApplyExecutor : IJobExecutor
{
    private readonly IReadOnlyDictionary<string, IUpdateProvider> _providers;
    private readonly IReadOnlyDictionary<PreflightStep, IPreflightAction> _preflight;
    private readonly IServiceController _services;

    public UpdateApplyExecutor(
        IEnumerable<IUpdateProvider> providers, IEnumerable<IPreflightAction> preflight, IServiceController services)
    {
        _providers = providers.ToDictionary(p => p.Channel);
        _preflight = preflight.ToDictionary(a => a.Step);
        _services = services;
    }

    public string JobType => "update.apply";

    public async Task<JobOutcome> ExecuteAsync(JobContext context, IJobSink sink, CancellationToken ct)
    {
        UpdateJobParams request;
        try
        {
            request = UpdateJobParamsSerializer.Deserialize(context.ParamsJson);
        }
        catch (JsonException ex)
        {
            return JobOutcome.Failure($"invalid update.apply params: {ex.Message}");
        }

        if (!_providers.TryGetValue(request.Channel, out var provider))
        {
            return JobOutcome.Failure($"no update provider for channel '{request.Channel}' on this node");
        }

        // Fail before touching anything if a required preflight step has no handler here.
        foreach (var step in request.Preflight)
        {
            if (!_preflight.ContainsKey(step))
            {
                return JobOutcome.Failure($"preflight step '{step}' is not available on this node");
            }
        }

        foreach (var step in request.Preflight)
        {
            await _preflight[step].RunAsync(sink, ct);
        }

        var brackets = request.How is UpdateHow.StopUpdateStart or UpdateHow.DrainThenUpdate
                       && !string.IsNullOrWhiteSpace(request.ServiceUnit);

        var stopped = false;
        if (brackets)
        {
            sink.Log(LogStream.Note, $"stopping {request.ServiceUnit}");
            await _services.StopAsync(request.ServiceUnit!, ct);
            stopped = true;
        }

        UpdateOutcome outcome;
        try
        {
            var plan = new UpdatePlan(request.Packages, AllowReboot: request.Reboot != RebootPolicy.Never);
            outcome = await provider.ApplyAsync(plan, sink, ct);
        }
        finally
        {
            // Always bring the service back up if we took it down, even on failure.
            if (stopped)
            {
                sink.Log(LogStream.Note, $"starting {request.ServiceUnit}");
                await _services.StartAsync(request.ServiceUnit!, ct);
            }
        }

        if (!outcome.Success)
        {
            return JobOutcome.Failure(outcome.FailReason ?? "update failed");
        }

        var reboot = UpdatePolicyResolver.ResolveReboot(request.Reboot, outcome.RebootRequired);
        sink.Log(LogStream.Note, RebootNote(reboot));
        return JobOutcome.Success();
    }

    private static string RebootNote(UpdatePolicyResolver.RebootAction action) => action switch
    {
        UpdatePolicyResolver.RebootAction.Reboot => "reboot required by policy (deferred to a reboot job)",
        UpdatePolicyResolver.RebootAction.PromptOperator => "reboot pending - operator confirmation required",
        _ => "no reboot required"
    };
}
