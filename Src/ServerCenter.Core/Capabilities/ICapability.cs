using ServerCenter.Core.Jobs;

namespace ServerCenter.Core.Capabilities;

// A game capability is descriptor-selected behavior. Primitives AND the plugin escape hatch
// implement the SAME contract, so callers cannot tell primitive-backed from plugin-backed
// apart (phase-0-contracts.md section 4). Each capability ships with an in-memory fake.
public interface ICapability
{
    CapabilityKind Kind { get; }
}

public enum CapabilityKind
{
    ConfigGen,
    SaveBackup,
    Stats,
    Shutdown,
    Readiness
}

public interface IConfigGenCapability : ICapability
{
    Task ApplyAsync(ConfigContext ctx, IJobSink sink, CancellationToken ct);
}

public interface ISaveBackupCapability : ICapability
{
    Task BackupAsync(SaveBackupContext ctx, IJobSink sink, CancellationToken ct);

    Task RestoreAsync(SaveRestoreContext ctx, IJobSink sink, CancellationToken ct);
}

public interface IStatsCapability : ICapability
{
    Task<ServerStats> ReadAsync(StatsContext ctx, CancellationToken ct);
}

public interface IShutdownCapability : ICapability
{
    Task GracefulShutdownAsync(ShutdownContext ctx, IJobSink sink, CancellationToken ct);
}

// Readiness != process-alive. Game-level "accepting players".
public interface IReadinessCapability : ICapability
{
    Task<Readiness> ProbeAsync(ReadinessContext ctx, CancellationToken ct);
}

// Context/result shapes are intentionally thin at scaffold time; fields land with each
// capability's phase. InstanceParams carries the class-vs-instance data (hostname, ports,
// rcon password, slots) resolved at run time.
public sealed record ConfigContext(IReadOnlyDictionary<string, string> InstanceParams);

public sealed record SaveBackupContext(string InstanceId, IReadOnlyDictionary<string, string> InstanceParams);

public sealed record SaveRestoreContext(string InstanceId, string SnapshotId);

public sealed record StatsContext(IReadOnlyDictionary<string, string> InstanceParams);

public sealed record ShutdownContext(int GraceSeconds, IReadOnlyDictionary<string, string> InstanceParams);

public sealed record ReadinessContext(IReadOnlyDictionary<string, string> InstanceParams);

public sealed record ServerStats(int? PlayerCount, IReadOnlyDictionary<string, string> Raw);

public enum Readiness
{
    Down,
    Starting,
    AcceptingPlayers
}
