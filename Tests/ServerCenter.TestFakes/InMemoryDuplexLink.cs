using System.Threading.Channels;
using ServerCenter.Contracts.V1;
using ServerCenter.Core.Transport;

namespace ServerCenter.TestFakes;

// A full in-memory duplex: the agent end (IAgentTransport) and the controller end
// (IControllerStream) wired to the same two channels crosswise. Lets AgentHandshake and
// ControllerHandshake run against each other at Tier 1 with no gRPC. DropStream() completes
// both channels to simulate the stream dropping (drives reconnect/resync paths).
public sealed class InMemoryDuplexLink : IDisposable
{
    private readonly Channel<AgentMessage> _agentToController = Channel.CreateUnbounded<AgentMessage>();
    private readonly Channel<ControllerMessage> _controllerToAgent = Channel.CreateUnbounded<ControllerMessage>();

    public IAgentTransport AgentSide { get; }

    public IControllerStream ControllerSide { get; }

    public InMemoryDuplexLink()
    {
        AgentSide = new AgentEnd(_controllerToAgent.Reader, _agentToController.Writer);
        ControllerSide = new ControllerEnd(_agentToController.Reader, _controllerToAgent.Writer);
    }

    public void DropStream()
    {
        _agentToController.Writer.TryComplete();
        _controllerToAgent.Writer.TryComplete();
    }

    public void Dispose() => DropStream();

    private sealed class AgentEnd(ChannelReader<ControllerMessage> incoming, ChannelWriter<AgentMessage> outgoing)
        : IAgentTransport
    {
        public IAsyncEnumerable<ControllerMessage> Incoming(CancellationToken ct) => incoming.ReadAllAsync(ct);

        public ValueTask SendAsync(AgentMessage message, CancellationToken ct) => outgoing.WriteAsync(message, ct);
    }

    private sealed class ControllerEnd(ChannelReader<AgentMessage> incoming, ChannelWriter<ControllerMessage> outgoing)
        : IControllerStream
    {
        public IAsyncEnumerable<AgentMessage> Incoming(CancellationToken ct) => incoming.ReadAllAsync(ct);

        public ValueTask SendAsync(ControllerMessage message, CancellationToken ct) => outgoing.WriteAsync(message, ct);
    }
}
