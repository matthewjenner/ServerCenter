using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServerCenter.Controller.Persistence;

namespace ServerCenter.Controller.Services;

// Auto-links managed guest nodes to their libvirt domain so the operator never has to link by hand:
// a guest with no domain yet is matched to a domain whose NAME equals the node id or display name
// (case-insensitive). Host nodes (node zero) are skipped - the hypervisor has no VM. Runs periodically
// so it catches nodes and domains that appear in either order. Registered only when libvirt is on.
public sealed class LibvirtAutoLinker(
    AgentNodeRepository nodes, LibvirtDomainStates domains, ILogger<LibvirtAutoLinker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await LinkOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "libvirt auto-link pass failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task LinkOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<NodeRow> rows = await nodes.ListNodesAsync(ct);
        IReadOnlyCollection<string> domainNames = domains.Names();

        foreach ((string nodeId, string domain) in MatchNodesToDomains(rows, domainNames))
        {
            await nodes.SetLibvirtDomainAsync(nodeId, domain, ct);
            logger.LogInformation("Auto-linked node {Node} to libvirt domain {Domain}", nodeId, domain);
        }
    }

    // Pure: an unlinked guest node is matched to a domain whose name equals the node id or display name
    // (case-insensitive). Skips host nodes and already-linked nodes. Testable with no libvirt/DB.
    public static IReadOnlyList<(string NodeId, string Domain)> MatchNodesToDomains(
        IReadOnlyList<NodeRow> nodes, IReadOnlyCollection<string> domainNames)
    {
        List<(string, string)> links = new List<(string, string)>();
        foreach (NodeRow node in nodes)
        {
            if (node.Kind == "host" || !string.IsNullOrEmpty(node.LibvirtDomain))
            {
                continue;
            }

            string? match = domainNames.FirstOrDefault(domain =>
                string.Equals(domain, node.NodeId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(domain, node.DisplayName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                links.Add((node.NodeId, match));
            }
        }

        return links;
    }
}
