using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServerCenter.Core.Primitives;

namespace ServerCenter.Controller.Services;

// Keeps LibvirtDomainStates current from the local libvirt: seed once with a full list, then follow
// the event stream (which VirshLibvirtHost implements as a poll for now). Registered only when
// libvirt is configured; a libvirt error is logged and does not crash the controller (the fleet
// simply shows VM state Unknown until it recovers).
public sealed class LibvirtStatePoller(
    ILibvirtHost libvirt, LibvirtDomainStates states, ILogger<LibvirtStatePoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            foreach (DomainInfo domain in await libvirt.ListDomainsAsync(stoppingToken))
            {
                states.Set(domain.Name, domain.State);
            }

            await foreach (DomainEvent change in libvirt.WatchEventsAsync(stoppingToken))
            {
                states.Set(change.NameOrUuid, change.State);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "libvirt state polling stopped; VM-running truth will be stale");
        }
    }
}
