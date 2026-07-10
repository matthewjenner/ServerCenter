using Grpc.Core;
using ServerCenter.Contracts.V1;
using ServerCenter.Controller.Persistence;
using ServerCenter.Core.Connection;
using ServerCenter.Core.Jobs;

namespace ServerCenter.Controller.Grpc;

// The server side of the bidi stream. Adapts the gRPC streams to IControllerStream, runs the
// connect handshake (version negotiation + resync), registers the agent/node, then the
// steady-state session pump. Returns when the agent disconnects. mTLS identity pinning threads
// in at a later Phase 1 ship; for now the agent id comes from the Hello and the node is 1:1
// with the agent.
public sealed class AgentLinkService(
    IControllerJobView jobs,
    IControllerSessionSink sink,
    AgentNodeRepository agents,
    TimeProvider clock,
    ILogger<AgentLinkService> logger) : AgentLink.AgentLinkBase
{
    public override async Task Connect(
        IAsyncStreamReader<AgentMessage> requestStream,
        IServerStreamWriter<ControllerMessage> responseStream,
        ServerCallContext context)
    {
        var ct = context.CancellationToken;
        var stream = new GrpcControllerStream(requestStream, responseStream);

        var handshake = await ControllerHandshake.PerformAsync(stream, jobs, ct: ct);
        if (!handshake.Established)
        {
            logger.LogWarning("Agent handshake rejected: {Reason}", handshake.RejectReason);
            return;
        }

        // Register the agent and its node (idempotent). The real mTLS fingerprint replaces
        // "unpinned" in the identity ship; kind/host detection lands with node-zero work.
        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        await agents.EnsureAgentAsync(handshake.AgentId, handshake.AgentId, "unpinned", now, ct);
        await agents.EnsureNodeAsync(handshake.AgentId, handshake.AgentId, "guest", "managed", now, ct);

        logger.LogInformation(
            "Agent {AgentId} connected (session {SessionId}, {ReconcileCount} jobs reconciled)",
            handshake.AgentId, handshake.SessionId, handshake.ReconcileActions.Count);

        try
        {
            await ControllerSessionPump.RunAsync(stream, handshake.AgentId, sink, ct);
        }
        finally
        {
            logger.LogInformation("Agent {AgentId} disconnected", handshake.AgentId);
        }
    }
}
