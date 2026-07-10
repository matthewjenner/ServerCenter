using System.Runtime.CompilerServices;
using ServerCenter.Core.Primitives;

namespace ServerCenter.Controller.Services;

// The ILibvirtHost used when libvirt is not configured (dev, or a controller not co-located with a
// hypervisor). Reads return "nothing known" (VM state stays Unknown); lifecycle actions fail loudly
// so a mis-dispatched VM job surfaces the misconfiguration instead of silently no-op'ing.
public sealed class NullLibvirtHost : ILibvirtHost
{
    public Task<IReadOnlyList<DomainInfo>> ListDomainsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DomainInfo>>([]);

    public Task<DomainInfo?> GetDomainAsync(string nameOrUuid, CancellationToken ct) =>
        Task.FromResult<DomainInfo?>(null);

    public Task StartAsync(string nameOrUuid, CancellationToken ct) => throw NotConfigured();

    public Task ShutdownAsync(string nameOrUuid, CancellationToken ct) => throw NotConfigured();

    public Task RebootAsync(string nameOrUuid, CancellationToken ct) => throw NotConfigured();

    public async IAsyncEnumerable<DomainEvent> WatchEventsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
    }

    private static InvalidOperationException NotConfigured() =>
        new("libvirt is not configured on this controller (set Libvirt:Enabled=true)");
}
