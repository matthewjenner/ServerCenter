using System.Threading.Channels;
using ServerCenter.Contracts.V1;
using ServerCenter.Core.Transport;

namespace ServerCenter.TestFakes;

// The highest-leverage fake: an in-memory IAgentTransport. Tests push ControllerMessages to
// the agent and read what the agent sent, with no gRPC. A chaos decorator (drop/delay/
// partition) can wrap this to exercise resync correctness at Tier 1 (testing.md, Tier 1).
public sealed class InMemoryAgentTransport : IAgentTransport
{
    private readonly Channel<ControllerMessage> _toAgent = Channel.CreateUnbounded<ControllerMessage>();
    private readonly Channel<AgentMessage> _fromAgent = Channel.CreateUnbounded<AgentMessage>();

    // IAgentTransport: what the agent consumes and produces.
    public IAsyncEnumerable<ControllerMessage> Incoming(CancellationToken ct) =>
        _toAgent.Reader.ReadAllAsync(ct);

    public ValueTask SendAsync(AgentMessage message, CancellationToken ct) =>
        _fromAgent.Writer.WriteAsync(message, ct);

    // Test-side controls.
    public ValueTask PushToAgentAsync(ControllerMessage message, CancellationToken ct = default) =>
        _toAgent.Writer.WriteAsync(message, ct);

    public IAsyncEnumerable<AgentMessage> ReadAgentOutput(CancellationToken ct = default) =>
        _fromAgent.Reader.ReadAllAsync(ct);

    // Simulate the stream dropping, so reconnect/resync paths can be driven deterministically.
    public void DropStream()
    {
        _toAgent.Writer.TryComplete();
        _fromAgent.Writer.TryComplete();
    }
}
