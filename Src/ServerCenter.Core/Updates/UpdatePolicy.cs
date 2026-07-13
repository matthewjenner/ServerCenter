namespace ServerCenter.Core.Updates;

// A controller-owned declarative update policy (brief 3.11, phase-0-contracts.md 5). Pure data
// over the update primitives: it says WHAT to update, HOW, WHEN, whether to REBOOT, what PREFLIGHT
// to run first, and whether it needs APPROVAL. It carries no behavior - UpdatePolicyResolver turns
// it into decisions, and an update.apply job executes it. Stored as versioned JSON keyed by
// (id, version), exactly like descriptors and recipes, so history reconstructs (what ran, under
// which policy version, last Tuesday).
public sealed record UpdatePolicy
{
    public required string Id { get; init; }

    public required int Version { get; init; }

    // The pluggable "what" provider selector (apt | plex | wu | ...). A free string, not an enum:
    // channels are open-ended and provider identity is data, not code.
    public required UpdateWhat What { get; init; }

    public UpdateHow How { get; init; } = UpdateHow.InPlace;

    public UpdateSchedule When { get; init; } = UpdateSchedule.Manual;

    public RebootPolicy Reboot { get; init; } = RebootPolicy.IfRequired;

    public IReadOnlyList<PreflightStep> Preflight { get; init; } = [];

    public ApprovalMode Approval { get; init; } = ApprovalMode.Auto;

    // The service to stop/start around the update (for How=stop-update-start / drain-then-update).
    // A per-policy DEFAULT so a profile like "plex" (plexmediaserver.service) is one-click; a dispatch
    // can still override it. Null when the update touches no service (apt, steamcmd).
    public string? ServiceUnit { get; init; }
}

public sealed record UpdateWhat(string Provider);

// A cron-windowed schedule. Manual means "never fires on its own; only an explicit operator
// trigger runs it". Window means "eligible to run autonomously while inside a window that opens at
// each cron occurrence and stays open for WindowMinutes". Cron is interpreted in UTC for now
// (per-node time zones are a later enhancement).
public sealed record UpdateSchedule
{
    public ScheduleMode Mode { get; init; }

    public string? Cron { get; init; }

    public int? WindowMinutes { get; init; }

    public static readonly UpdateSchedule Manual = new() { Mode = ScheduleMode.Manual };
}

public enum ScheduleMode
{
    Manual,
    Window
}

public enum UpdateHow
{
    InPlace,          // update the package in place, service keeps running where it can
    StopUpdateStart,  // stop the service, update, start it again
    DrainThenUpdate   // drain (e.g. players via RCON), then stop-update-start
}

public enum RebootPolicy
{
    Never,
    IfRequired,   // reboot only if the provider reports a reboot is pending
    AlwaysAfter,
    Prompt        // never reboot autonomously; surface a prompt to the operator
}

// Ordered pre-update actions. Each maps to an existing primitive (notify, snapshot, the RCON
// drain, quiesce-before-backup) so no new code per policy.
public enum PreflightStep
{
    Notify,
    SnapshotFirst,
    DrainPlayersViaRcon,
    Quiesce
}

public enum ApprovalMode
{
    Auto,
    RequireConfirmation
}
