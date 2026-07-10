using ServerCenter.Contracts.V1;
using ServerCenter.Controller.Persistence;
using ServerCenter.Core.Connection;

namespace ServerCenter.Controller.Services;

// The controller's session sink: delegates live heartbeat/status to the in-memory presence store
// and PERSISTS job progress/results into the job spine (SQLite). Replaces the presence-only sink.
public sealed class PersistingSessionSink(AgentPresenceStore presence, JobRepository jobs, TimeProvider clock)
    : IControllerSessionSink
{
    public Task OnHeartbeatAsync(string agentId, Heartbeat heartbeat, CancellationToken ct) =>
        presence.OnHeartbeatAsync(agentId, heartbeat, ct);

    public Task OnStatusAsync(string agentId, NodeStatus status, CancellationToken ct) =>
        presence.OnStatusAsync(agentId, status, ct);

    public async Task OnJobProgressAsync(string agentId, JobProgress progress, CancellationToken ct)
    {
        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();

        if (progress.Log is not null)
        {
            await jobs.AppendLogAsync(
                progress.JobId, progress.Seq, MapStream(progress.Log.Stream), progress.Log.Line,
                progress.Log.TsUnixMs != 0 ? progress.Log.TsUnixMs : now, ct);
        }

        await jobs.ApplyProgressAsync(
            progress.JobId,
            progress.Pct >= 0 ? progress.Pct : null,
            string.IsNullOrEmpty(progress.Note) ? null : progress.Note,
            now, ct);

        if (progress.Seq > 0)
        {
            await jobs.AckLogAsync(progress.JobId, progress.Seq, ct);
        }
    }

    public async Task OnCommandResultAsync(string agentId, CommandResult result, CancellationToken ct)
    {
        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        await jobs.UpdateStateAsync(
            result.JobId, MapState(result.FinalState),
            string.IsNullOrEmpty(result.FailReason) ? null : result.FailReason, now, ct);
    }

    private static Core.Jobs.LogStream MapStream(LogStream stream) => stream switch
    {
        LogStream.Stdout => Core.Jobs.LogStream.Stdout,
        LogStream.Stderr => Core.Jobs.LogStream.Stderr,
        _ => Core.Jobs.LogStream.Note
    };

    private static Core.Jobs.JobState MapState(JobState state) => state switch
    {
        JobState.Succeeded => Core.Jobs.JobState.Succeeded,
        JobState.Failed => Core.Jobs.JobState.Failed,
        JobState.Timedout => Core.Jobs.JobState.TimedOut,
        JobState.Cancelled => Core.Jobs.JobState.Cancelled,
        JobState.Running => Core.Jobs.JobState.Running,
        _ => Core.Jobs.JobState.Failed
    };
}
