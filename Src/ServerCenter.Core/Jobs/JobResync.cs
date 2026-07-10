namespace ServerCenter.Core.Jobs;

// Domain types for the resync handshake (phase-0-contracts.md 2.3), independent of the wire
// enums so the reconciler is pure and Tier 1 testable.

// The controller's view of a job it believes is still open for an agent.
public sealed record ControllerOpenJob(string JobId, bool Requeueable);

// What the agent reports about a job it knows on reconnect.
public enum AgentJobLocalState
{
    StillRunning,
    FinishedSucceeded,
    FinishedFailed,
    Unknown // agent lost its state (e.g. rebuilt)
}

public sealed record AgentResyncEntry(string JobId, AgentJobLocalState LocalState, long LastSeq);

// The reconciliation outcome the controller must apply per job.
public enum ReconcileOutcome
{
    Resume,             // controller running + agent still running -> replay from last_acked_seq
    CloseSucceeded,     // finished while we were gone
    CloseFailed,        // finished (failed) while we were gone
    FailLost,           // agent lost state and the job is not safely requeueable
    Requeue,            // agent lost state but the job type is idempotently requeueable
    DropUnknownToAgent  // agent reported a job the controller has no record of
}

public sealed record ReconcileAction(string JobId, ReconcileOutcome Outcome);
