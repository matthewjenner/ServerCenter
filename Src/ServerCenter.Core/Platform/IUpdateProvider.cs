using ServerCenter.Core.Jobs;

namespace ServerCenter.Core.Platform;

// The pluggable "what" provider. apt AND an app channel (Plex) both implement this so the
// abstraction is proven non-apt from day one and cannot ossify as "apt with extra steps".
// Ships with an in-memory fake.
public interface IUpdateProvider
{
    string Channel { get; } // "apt" | "plex" | "wu" | ...

    Task<IReadOnlyList<AvailableUpdate>> CheckAsync(CancellationToken ct);

    Task<UpdateOutcome> ApplyAsync(UpdatePlan plan, IJobSink sink, CancellationToken ct);

    Task<bool> RebootRequiredAsync(CancellationToken ct);
}

public sealed record AvailableUpdate(string Package, string CurrentVersion, string TargetVersion);

public sealed record UpdatePlan(IReadOnlyList<string> Packages, bool AllowReboot);

public sealed record UpdateOutcome(bool Success, bool RebootRequired, string? FailReason);
