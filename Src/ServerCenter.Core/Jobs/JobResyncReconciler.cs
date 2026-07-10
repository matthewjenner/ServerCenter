namespace ServerCenter.Core.Jobs;

// The top refactor trap made into a pure, exhaustively testable function (phase-0-contracts.md
// 2.3). Given what the controller believes is open and what the agent reports on reconnect,
// produce the exact set of actions to apply. Deterministic order: controller's open jobs
// first (in input order), then any surplus the agent reported that the controller does not know.
public static class JobResyncReconciler
{
    public static IReadOnlyList<ReconcileAction> Reconcile(
        IReadOnlyList<ControllerOpenJob> open,
        IReadOnlyList<AgentResyncEntry> report)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(report);

        Dictionary<string, AgentResyncEntry> reportById = new Dictionary<string, AgentResyncEntry>(report.Count);
        foreach (AgentResyncEntry entry in report)
        {
            reportById[entry.JobId] = entry; // last write wins on duplicate ids
        }

        List<ReconcileAction> actions = new List<ReconcileAction>(open.Count + report.Count);
        HashSet<string> openIds = new HashSet<string>(open.Count);

        foreach (ControllerOpenJob job in open)
        {
            openIds.Add(job.JobId);

            // Controller has it running, but the agent never mentioned it OR explicitly lost
            // its state: it is lost across the disconnect. Requeue only if the job type is safe.
            if (!reportById.TryGetValue(job.JobId, out AgentResyncEntry? entry) ||
                entry.LocalState == AgentJobLocalState.Unknown)
            {
                actions.Add(new ReconcileAction(
                    job.JobId,
                    job.Requeueable ? ReconcileOutcome.Requeue : ReconcileOutcome.FailLost));
                continue;
            }

            actions.Add(entry.LocalState switch
            {
                AgentJobLocalState.StillRunning => new ReconcileAction(job.JobId, ReconcileOutcome.Resume),
                AgentJobLocalState.FinishedSucceeded => new ReconcileAction(job.JobId, ReconcileOutcome.CloseSucceeded),
                AgentJobLocalState.FinishedFailed => new ReconcileAction(job.JobId, ReconcileOutcome.CloseFailed),
                _ => new ReconcileAction(job.JobId, job.Requeueable ? ReconcileOutcome.Requeue : ReconcileOutcome.FailLost)
            });
        }

        // Anything the agent reported that the controller has no record of: instruct the agent
        // to drop it (should be rare; the controller is the id source of record).
        foreach (AgentResyncEntry entry in report)
        {
            if (!openIds.Contains(entry.JobId))
            {
                actions.Add(new ReconcileAction(entry.JobId, ReconcileOutcome.DropUnknownToAgent));
            }
        }

        return actions;
    }
}
