using System.Text.Json;
using Microsoft.Extensions.Logging;
using ServerCenter.Controller.Persistence;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Primitives;

namespace ServerCenter.Controller.Services;

// VM lifecycle as CONTROLLER-driven jobs (brief 3.2/3.3): unlike every other job, these execute on
// the controller against the local libvirt (ILibvirtHost), not pushed to an agent - the host control
// plane is strictly controller-driven. Still a real persisted job (visible in JobView, survives in
// history). The libvirt verbs return quickly (they request a transition; the domain changes async and
// the state poller reflects it), so execution runs inline: Queued -> Running -> terminal.
public sealed class VmLifecycleService(
    ILibvirtHost libvirt,
    JobRepository jobs,
    AgentNodeRepository nodes,
    TimeProvider clock,
    ILogger<VmLifecycleService> logger)
{
    public async Task<VmDispatchResult> DispatchAsync(string nodeId, VmAction action, CancellationToken ct)
    {
        var node = await nodes.GetNodeAsync(nodeId, ct);
        if (node is null)
        {
            return VmDispatchResult.NotFound($"node '{nodeId}' not found");
        }

        if (string.IsNullOrEmpty(node.LibvirtDomain))
        {
            return VmDispatchResult.NoDomain($"node '{nodeId}' has no libvirt domain linked");
        }

        var jobId = Guid.NewGuid().ToString("N");
        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        await jobs.InsertAsync(new Job
        {
            Id = jobId,
            NodeId = nodeId,
            Type = JobType(action),
            ParamsJson = JsonSerializer.Serialize(new { domain = node.LibvirtDomain, action = action.ToString() }),
            State = JobState.Queued,
            Cancellable = false,
            Requeueable = false,
            CreatedAtUnixMs = now
        }, ct);

        await ExecuteAsync(jobId, node.LibvirtDomain, action, ct);
        return VmDispatchResult.Dispatched(jobId);
    }

    private async Task ExecuteAsync(string jobId, string domain, VmAction action, CancellationToken ct)
    {
        await jobs.ApplyProgressAsync(jobId, null, $"{action} {domain}", clock.GetUtcNow().ToUnixTimeMilliseconds(), ct);
        try
        {
            await (action switch
            {
                VmAction.Start => libvirt.StartAsync(domain, ct),
                VmAction.Stop => libvirt.ShutdownAsync(domain, ct),
                _ => libvirt.RebootAsync(domain, ct)
            });
            await jobs.UpdateStateAsync(jobId, JobState.Succeeded, null, clock.GetUtcNow().ToUnixTimeMilliseconds(), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "vm {Action} {Domain} failed", action, domain);
            await jobs.UpdateStateAsync(jobId, JobState.Failed, ex.Message, clock.GetUtcNow().ToUnixTimeMilliseconds(), ct);
        }
    }

    private static string JobType(VmAction action) => action switch
    {
        VmAction.Start => "vm.start",
        VmAction.Stop => "vm.stop",
        _ => "vm.restart"
    };
}

public enum VmAction
{
    Start,
    Stop,
    Restart
}

public enum VmDispatchOutcome
{
    Dispatched,
    NotFound,
    NoDomain
}

public sealed record VmDispatchResult(VmDispatchOutcome Outcome, string? JobId, string? Reason)
{
    public static VmDispatchResult Dispatched(string jobId) => new(VmDispatchOutcome.Dispatched, jobId, null);

    public static VmDispatchResult NotFound(string reason) => new(VmDispatchOutcome.NotFound, null, reason);

    public static VmDispatchResult NoDomain(string reason) => new(VmDispatchOutcome.NoDomain, null, reason);
}
