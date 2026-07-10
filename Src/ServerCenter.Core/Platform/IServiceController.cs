namespace ServerCenter.Core.Platform;

// Per-OS service control. Linux via DBus/busctl, Windows via System.ServiceProcess/SCM.
// Prefer structured state and change subscription over parsing `systemctl status` text.
// Ships with an in-memory fake (testability constraint, phase-0-contracts.md section 3).
public interface IServiceController
{
    Task<ServiceState> GetStateAsync(string unit, CancellationToken ct);

    Task StartAsync(string unit, CancellationToken ct);

    Task StopAsync(string unit, CancellationToken ct);

    Task RestartAsync(string unit, CancellationToken ct);

    Task EnsureEnabledAsync(string unit, bool enabled, CancellationToken ct);

    IAsyncEnumerable<ServiceState> WatchAsync(string unit, CancellationToken ct);
}

// Service being Active is NOT the same as game-level readiness (see IReadinessCapability).
public enum ServiceState
{
    Active,
    Inactive,
    Failed,
    Activating,
    Deactivating,
    Unknown
}
