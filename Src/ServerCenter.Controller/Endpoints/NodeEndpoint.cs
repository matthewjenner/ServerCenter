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
    }
}

public sealed record SetLibvirtDomainRequest(string? Domain);
