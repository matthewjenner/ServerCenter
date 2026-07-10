using ServerCenter.Controller.Persistence;
using ServerCenter.Core.Jobs;

namespace ServerCenter.Controller.Services;

// The resync handshake's view of the job store, backed by SQLite so open jobs and their
// reconciled outcomes survive a controller restart. Replaces the in-memory placeholder.
public sealed class SqliteControllerJobView(JobRepository jobs, TimeProvider clock) : IControllerJobView
{
    public async Task<IReadOnlyList<ControllerOpenJob>> GetOpenJobsAsync(string agentId, CancellationToken ct)
    {
        IReadOnlyList<Job> open = await jobs.GetOpenJobsForAgentAsync(agentId, ct);
        List<ControllerOpenJob> result = new List<ControllerOpenJob>(open.Count);
        foreach (Job job in open)
        {
            result.Add(new ControllerOpenJob(job.Id, job.Requeueable));
        }

        return result;
    }

    public async Task ApplyAsync(ReconcileAction action, CancellationToken ct)
    {
        long now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        switch (action.Outcome)
        {
            case ReconcileOutcome.CloseSucceeded:
                await jobs.UpdateStateAsync(action.JobId, JobState.Succeeded, null, now, ct);
                break;
            case ReconcileOutcome.CloseFailed:
                await jobs.UpdateStateAsync(action.JobId, JobState.Failed, "failed while disconnected", now, ct);
                break;
            case ReconcileOutcome.FailLost:
                await jobs.UpdateStateAsync(action.JobId, JobState.Failed, "lost_after_disconnect", now, ct);
                break;
            case ReconcileOutcome.Requeue:
                await jobs.UpdateStateAsync(action.JobId, JobState.Queued, null, null, ct);
                break;
            case ReconcileOutcome.Resume:
                // Still running on the agent; the controller keeps its record as-is and resumes streaming.
                break;
            case ReconcileOutcome.DropUnknownToAgent:
                // The controller has no record of this job; nothing to persist.
                break;
            default:
                break;
        }
    }
}
