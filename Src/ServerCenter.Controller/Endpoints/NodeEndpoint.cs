using ServerCenter.Controller.Persistence;

namespace ServerCenter.Controller.Endpoints;

// Operator/dev node administration. Linking a node to its libvirt domain turns on the VM-running half
// of dual-truth for it (until provisioning sets this automatically in Phase 7). Operator auth deferred.
public static class NodeEndpoint
{
    public static void MapNodes(this WebApplication app)
    {
        app.MapPost("/nodes/{nodeId}/libvirt-domain",
            async (string nodeId, SetLibvirtDomainRequest request, AgentNodeRepository nodes, CancellationToken ct) =>
            {
                await nodes.SetLibvirtDomainAsync(nodeId, request.Domain, ct);
                return Results.Ok(new { nodeId, request.Domain });
            });

        // Record a node the controller is about to bring up (lifecycle 'provisioning'). Its agent
        // flips it to 'managed' on first check-in. The actual VM define + cloud-init is out of band.
        app.MapPost("/nodes/provision",
            async (ProvisionNodeRequest request, AgentNodeRepository nodes, TimeProvider clock, CancellationToken ct) =>
            {
                await nodes.ProvisionNodeAsync(
                    request.NodeId, request.Kind ?? "guest", request.LibvirtDomain, request.OsFamily,
                    clock.GetUtcNow().ToUnixTimeMilliseconds(), ct);
                return Results.Ok(new { request.NodeId, lifecycle = "provisioning" });
            });
    }
}

public sealed record SetLibvirtDomainRequest(string? Domain);

public sealed record ProvisionNodeRequest(string NodeId, string? Kind, string? LibvirtDomain, string? OsFamily);
