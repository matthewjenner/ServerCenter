using ServerCenter.Core.Primitives;

namespace ServerCenter.TestFakes;

// An in-memory VM-lifecycle plane: seeded domains with settable state, mutated by start/shutdown/
// reboot, so the dual-truth fleet view and VM-lifecycle jobs are Tier 1 testable with no libvirt.
public sealed class FakeLibvirtHost : ILibvirtHost
{
    private readonly Dictionary<string, DomainInfo> _domains = new();

    public List<(string Verb, string Domain)> Calls { get; } = [];

    public void Seed(string name, string uuid, DomainState state) => _domains[name] = new DomainInfo(name, uuid, state);

    public Task<IReadOnlyList<DomainInfo>> ListDomainsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DomainInfo>>(_domains.Values.ToList());

    public Task<DomainInfo?> GetDomainAsync(string nameOrUuid, CancellationToken ct) =>
        Task.FromResult(_domains.GetValueOrDefault(nameOrUuid));

    public Task StartAsync(string nameOrUuid, CancellationToken ct) => Transition(nameOrUuid, "start", DomainState.Running);

    public Task ShutdownAsync(string nameOrUuid, CancellationToken ct) => Transition(nameOrUuid, "shutdown", DomainState.ShutOff);

    public Task RebootAsync(string nameOrUuid, CancellationToken ct) => Transition(nameOrUuid, "reboot", DomainState.Running);

    public async IAsyncEnumerable<DomainEvent> WatchEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
    }

    private Task Transition(string name, string verb, DomainState state)
    {
        Calls.Add((verb, name));
        var uuid = _domains.TryGetValue(name, out var existing) ? existing.Uuid : string.Empty;
        _domains[name] = new DomainInfo(name, uuid, state);
        return Task.CompletedTask;
    }
}
