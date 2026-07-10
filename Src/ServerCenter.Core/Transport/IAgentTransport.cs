using ServerCenter.Contracts.V1;

namespace ServerCenter.Core.Transport;

// Testability constraint (phase-0-contracts.md 1.4): the bidi stream sits behind this seam
// from message one. Production wires the real gRPC channel; tests inject an in-memory
// transport and decorate it with a chaos layer (drop/delay/partition) so resync correctness
// is exercisable at Tier 1. Both directions of the stream flow through here.
public interface IAgentTransport
{
    IAsyncEnumerable<ControllerMessage> Incoming(CancellationToken ct);

    ValueTask SendAsync(AgentMessage message, CancellationToken ct);
}
