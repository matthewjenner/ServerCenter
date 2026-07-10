namespace ServerCenter.Core.Jobs;

// The allowed transitions from phase-0-contracts.md 2.1. Kept as pure logic so Tier 1 can
// hammer it. Cancellability is enforced by the caller per job type; this only encodes which
// state transitions are structurally legal.
public static class JobStateMachine
{
    public static bool IsTerminal(JobState state) => state is
        JobState.Succeeded or JobState.Failed or JobState.TimedOut or JobState.Cancelled;

    public static bool CanTransition(JobState from, JobState to) => (from, to) switch
    {
        (JobState.Queued, JobState.Running) => true,
        (JobState.Queued, JobState.Cancelled) => true,      // cancel while queued: always ok
        (JobState.Running, JobState.Succeeded) => true,
        (JobState.Running, JobState.Failed) => true,
        (JobState.Running, JobState.TimedOut) => true,
        (JobState.Running, JobState.Cancelled) => true,     // only if the job type allows it
        _ => false
    };
}
