using System.Security.Cryptography.X509Certificates;
using Grpc.Core;
using ServerCenter.Contracts.V1;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Connection;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Transport;

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
    ConnectedAgents connected,
    AgentAuthorizer authorizer,
    AgentSecurityOptions security,
    AgentPresenceStore presence,
    TimeProvider clock,
    ILogger<AgentLinkService> logger) : AgentLink.AgentLinkBase
{
    public override async Task Connect(
        IAsyncStreamReader<AgentMessage> requestStream,
        IServerStreamWriter<ControllerMessage> responseStream,
        ServerCallContext context)
    {
        CancellationToken ct = context.CancellationToken;
        GrpcControllerStream stream = new GrpcControllerStream(requestStream, responseStream);

        ControllerHandshakeResult handshake = await ControllerHandshake.PerformAsync(stream, jobs, ct: ct);
        if (!handshake.Established)
        {
            logger.LogWarning("Agent handshake rejected: {Reason}", handshake.RejectReason);
            return;
        }

        long now = clock.GetUtcNow().ToUnixTimeMilliseconds();

        // Live diagnostics from the Hello, refreshed on every (re)connect (surfaced in the fleet view).
        presence.RecordConnect(handshake.AgentId, handshake.AgentVersion, handshake.OsFamily, handshake.Arch);

        if (security.RequireClientCertificate)
        {
            // mTLS enforced: the presented client cert must authenticate the claimed agent id.
            X509Certificate2? clientCertificate = context.GetHttpContext()?.Connection.ClientCertificate;
            if (!await authorizer.AuthorizeAsync(clientCertificate, handshake.AgentId, ct))
            {
                logger.LogWarning("Agent {AgentId} failed client-certificate authorization", handshake.AgentId);
                await stream.SendAsync(
                    new ControllerMessage
                    {
                        Envelope = Envelopes.New(),
                        Goodbye = new Goodbye { Reason = GoodbyeReason.Revoked, Message = "client certificate not authorized" }
                    },
                    ct);
                return;
            }

            // Identity already exists from enrollment; just ensure the node with its kind.
            await agents.EnsureNodeAsync(handshake.AgentId, handshake.AgentId, handshake.NodeKind, "managed", now, ct);
        }
        else
        {
            // Dev / no-mTLS: register with an unpinned identity so the flow works over plaintext.
            await agents.EnsureAgentAsync(handshake.AgentId, handshake.AgentId, "unpinned", now, ct);
            await agents.EnsureNodeAsync(handshake.AgentId, handshake.AgentId, handshake.NodeKind, "managed", now, ct);
        }

        // Provisioning -> managed handoff (brief 3.13): a node pre-created in 'provisioning' (its VM
        // defined + cloud-init'd) flips to 'managed' on its agent's first check-in. No-op otherwise.
        await agents.MarkManagedOnCheckInAsync(handshake.AgentId, handshake.AgentId, ct);

        logger.LogInformation(
            "Agent {AgentId} connected (session {SessionId}, {ReconcileCount} jobs reconciled)",
            handshake.AgentId, handshake.SessionId, handshake.ReconcileActions.Count);

        // Make this stream reachable for command push (job dispatch) for as long as it is up.
        connected.Register(handshake.AgentId, stream);
        try
        {
            await ControllerSessionPump.RunAsync(stream, handshake.AgentId, sink, ct);
        }
        finally
        {
            connected.Unregister(handshake.AgentId, stream);
            logger.LogInformation("Agent {AgentId} disconnected", handshake.AgentId);
        }
    }
}
