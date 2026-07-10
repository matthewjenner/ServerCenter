namespace ServerCenter.Core.Primitives;

// The VM-lifecycle plane (phase-0-contracts.md; brief 3.2/3.3). No good first-party .NET
// libvirt binding, so the real impl uses the local socket / virsh with structured XML I/O,
// wrapped here so it is swappable. Invisible to containers by definition: XML/command
// generation is unit-tested against the fake (Tier 1), the real thing only at Tier 3.
public interface ILibvirtHost
{
    Task<IReadOnlyList<DomainInfo>> ListDomainsAsync(CancellationToken ct);

    Task<DomainInfo?> GetDomainAsync(string nameOrUuid, CancellationToken ct);

    Task StartAsync(string nameOrUuid, CancellationToken ct);

    Task ShutdownAsync(string nameOrUuid, CancellationToken ct);

    Task RebootAsync(string nameOrUuid, CancellationToken ct);

    // Live truth: agent-online and VM-running are independent facts (dual-truth, brief 3.7).
    IAsyncEnumerable<DomainEvent> WatchEventsAsync(CancellationToken ct);
}

public sealed record DomainInfo(string Name, string Uuid, DomainState State);

public sealed record DomainEvent(string NameOrUuid, DomainState State, long TsUnixMs);

public enum DomainState
{
    NoState,
    Running,
    Paused,
    Shutdown,
    ShutOff,
    Crashed,
    Unknown
}
