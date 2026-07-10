using ServerCenter.Core.Platform;

namespace ServerCenter.Agent.Windows;

// Real impl (Phase 8) uses System.ServiceProcess/SCM. Stub against the proven interface for
// now. Session-0 SCM semantics are validated only at Tier 3 (real Windows VM), per testing.md.
public sealed class WindowsServiceController : IServiceController
{
    public Task<ServiceState> GetStateAsync(string unit, CancellationToken ct) =>
        throw new NotImplementedException("Windows service control lands in Phase 8.");

    public Task StartAsync(string unit, CancellationToken ct) =>
        throw new NotImplementedException("Windows service control lands in Phase 8.");

    public Task StopAsync(string unit, CancellationToken ct) =>
        throw new NotImplementedException("Windows service control lands in Phase 8.");

    public Task RestartAsync(string unit, CancellationToken ct) =>
        throw new NotImplementedException("Windows service control lands in Phase 8.");

    public Task EnsureEnabledAsync(string unit, bool enabled, CancellationToken ct) =>
        throw new NotImplementedException("Windows service control lands in Phase 8.");

    // No SCM equivalent of daemon-reload; a no-op keeps the interface satisfiable.
    public Task ReloadAsync(CancellationToken ct) => Task.CompletedTask;

    public IAsyncEnumerable<ServiceState> WatchAsync(string unit, CancellationToken ct) =>
        throw new NotImplementedException("Windows service control lands in Phase 8.");
}
