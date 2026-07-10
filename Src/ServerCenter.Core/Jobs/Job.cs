namespace ServerCenter.Core.Jobs;

// The domain job (phase-0-contracts.md section 2). The controller is the system of record;
// it assigns the id and persists at Queued before the Command is sent down.
public sealed record Job
{
    public required string Id { get; init; }          // ulid, controller-assigned
    public required string NodeId { get; init; }
    public required string Type { get; init; }        // e.g. service.restart, update.apply
    public required string ParamsJson { get; init; }
    public JobState State { get; init; } = JobState.Queued;
    public int? ProgressPct { get; init; }            // null = indeterminate
    public string? ProgressNote { get; init; }
    public bool Cancellable { get; init; }            // per job type
    public bool Requeueable { get; init; }            // drives resync 'unknown' handling
    public long LastAckedSeq { get; init; }
    public long CreatedAtUnixMs { get; init; }
    public long? StartedAtUnixMs { get; init; }
    public long? TerminalAtUnixMs { get; init; }      // set exactly once, on terminal entry
    public string? FailReason { get; init; }
    public string? CorrelationId { get; init; }
}
