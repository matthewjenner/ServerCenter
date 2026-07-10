using Grpc.Core;
using ServerCenter.Contracts.V1;
using ServerCenter.Core.Connection;
using ServerCenter.Core.Jobs;

namespace ServerCenter.Controller.Grpc;

// The server side of the bidi stream. Adapts the gRPC streams to IControllerStream, runs the
// connect handshake (version negotiation + resync), then the steady-state session pump. The
// method returns when the agent disconnects. mTLS identity pinning threads in at a later
// Phase 1 ship; for now the agent id comes from the Hello.
public sealed class AgentLinkService(
    IControllerJobView jobs,
    IControllerSessionSink sink,
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
