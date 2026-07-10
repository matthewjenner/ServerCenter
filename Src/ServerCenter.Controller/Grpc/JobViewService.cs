using System.Text.Json;
using Grpc.Core;
using ServerCenter.Contracts.V1;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;

namespace ServerCenter.Controller.Grpc;

// Operator-facing job endpoint for the dashboard: streams recent jobs and triggers a service
// restart. Not client-cert authenticated (operator auth deferred, like FleetView). JobState here
// is the proto enum; the domain enum is fully qualified to avoid the name clash.
public sealed class JobViewService(JobRepository jobs, JobDispatcher dispatcher, TimeProvider clock) : JobView.JobViewBase
{
    private static readonly TimeSpan PushInterval = TimeSpan.FromSeconds(2);

    public override async Task WatchJobs(
        WatchJobsRequest request, IServerStreamWriter<JobListSnapshot> responseStream, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        while (!ct.IsCancellationRequested)
        {
            await responseStream.WriteAsync(await BuildAsync(ct), ct);
            try
            {
                await Task.Delay(PushInterval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public override async Task<RestartServiceResponse> RestartService(RestartServiceRequest request, ServerCallContext context)
    {
        var paramsJson = JsonSerializer.Serialize(new { unit = request.Unit });
        var jobId = await dispatcher.DispatchAsync(
            request.AgentId, "service.restart", paramsJson, cancellable: false, requeueable: false, context.CancellationToken);
        return new RestartServiceResponse { JobId = jobId };
    }

    private async Task<JobListSnapshot> BuildAsync(CancellationToken ct)
    {
        var recent = await jobs.ListRecentJobsAsync(50, ct);
        var snapshot = new JobListSnapshot { GeneratedUnixMs = clock.GetUtcNow().ToUnixTimeMilliseconds() };
        foreach (var job in recent)
        {
            snapshot.Jobs.Add(Map(job));
        }

        return snapshot;
    }

    private static JobInfo Map(Core.Jobs.Job job) => new()
    {
        JobId = job.Id,
        NodeId = job.NodeId,
        Type = job.Type,
        State = MapState(job.State),
        ProgressPct = job.ProgressPct ?? -1,
        ProgressNote = job.ProgressNote ?? string.Empty,
        FailReason = job.FailReason ?? string.Empty,
        CreatedUnixMs = job.CreatedAtUnixMs,
        TerminalUnixMs = job.TerminalAtUnixMs ?? 0
    };

    private static JobState MapState(Core.Jobs.JobState state) => state switch
    {
        Core.Jobs.JobState.Queued => JobState.Queued,
        Core.Jobs.JobState.Running => JobState.Running,
        Core.Jobs.JobState.Succeeded => JobState.Succeeded,
        Core.Jobs.JobState.Failed => JobState.Failed,
        Core.Jobs.JobState.TimedOut => JobState.Timedout,
        Core.Jobs.JobState.Cancelled => JobState.Cancelled,
        _ => JobState.Unspecified
    };
}
