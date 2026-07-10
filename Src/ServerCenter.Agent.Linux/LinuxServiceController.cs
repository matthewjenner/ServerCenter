using ServerCenter.Core.Platform;

namespace ServerCenter.Agent.Linux;

// Real impl (Phase 3) uses DBus/busctl for structured state and change subscription rather
// than parsing `systemctl status`. Stub against the proven interface for now.
public sealed class LinuxServiceController : IServiceController
{
    public Task<ServiceState> GetStateAsync(string unit, CancellationToken ct) =>
        throw new NotImplementedException("Linux service control lands in Phase 3.");

    public Task StartAsync(string unit, CancellationToken ct) =>
        throw new NotImplementedException("Linux service control lands in Phase 3.");

    public Task StopAsync(string unit, CancellationToken ct) =>
        throw new NotImplementedException("Linux service control lands in Phase 3.");

    public Task RestartAsync(string unit, CancellationToken ct) =>
        throw new NotImplementedException("Linux service control lands in Phase 3.");

    public Task EnsureEnabledAsync(string unit, bool enabled, CancellationToken ct) =>
        throw new NotImplementedException("Linux service control lands in Phase 3.");

    public IAsyncEnumerable<ServiceState> WatchAsync(string unit, CancellationToken ct) =>
        throw new NotImplementedException("Linux service control lands in Phase 3.");
}
