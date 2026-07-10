using ServerCenter.Contracts.V1;

namespace ServerCenter.Core.Transport;

// The controller end of the bidi stream, the mirror of IAgentTransport. The real gRPC
// AgentLinkService adapts IAsyncStreamReader<AgentMessage> / IServerStreamWriter<
// ControllerMessage> to this; tests wire it to an in-memory duplex link so both ends run
// against each other at Tier 1 with no gRPC.
public interface IControllerStream
{
    IAsyncEnumerable<AgentMessage> Incoming(CancellationToken ct);

    ValueTask SendAsync(ControllerMessage message, CancellationToken ct);
}
