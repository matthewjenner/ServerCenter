namespace ServerCenter.Core.Jobs;

// Domain job state, independent of the wire enum (Contracts.V1.JobState) so the state
// machine logic lives in Core and is unit-testable without the transport.
public enum JobState
{
    Queued,
    Running,
    Succeeded,
    Failed,
    TimedOut,
    Cancelled
}
