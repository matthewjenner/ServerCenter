using System.Collections.Concurrent;
using ServerCenter.Core.Platform;

namespace ServerCenter.TestFakes;

// An in-memory IServiceController: holds unit state in a dictionary and mutates it on start/
// stop/restart. Lets job-spine tests drive service-restart flows with no systemd/SCM.
public sealed class FakeServiceController : IServiceController
{
    private readonly ConcurrentDictionary<string, ServiceState> _state = new();

    // Ordered log of verb+unit calls, so tests can assert e.g. a stop-then-start bracket.
    public List<(string Verb, string Unit)> Calls { get; } = [];

    public void Seed(string unit, ServiceState state) => _state[unit] = state;

    public Task<ServiceState> GetStateAsync(string unit, CancellationToken ct) =>
        Task.FromResult(_state.GetValueOrDefault(unit, ServiceState.Unknown));

    public Task StartAsync(string unit, CancellationToken ct)
    {
        Calls.Add(("start", unit));
        _state[unit] = ServiceState.Active;
        return Task.CompletedTask;
    }

    public Task StopAsync(string unit, CancellationToken ct)
    {
        Calls.Add(("stop", unit));
        _state[unit] = ServiceState.Inactive;
        return Task.CompletedTask;
    }

    public Task RestartAsync(string unit, CancellationToken ct)
    {
        Calls.Add(("restart", unit));
        _state[unit] = ServiceState.Active;
        return Task.CompletedTask;
    }

    public Task EnsureEnabledAsync(string unit, bool enabled, CancellationToken ct) =>
        Task.CompletedTask;

    public async IAsyncEnumerable<ServiceState> WatchAsync(
        string unit,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        yield return _state.GetValueOrDefault(unit, ServiceState.Unknown);
        await Task.CompletedTask;
    }
}
