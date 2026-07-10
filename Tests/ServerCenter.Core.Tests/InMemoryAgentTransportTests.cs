using AwesomeAssertions;
using ServerCenter.Contracts.V1;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Core.Tests;

// Proves the transport seam is drivable in-memory: the foundation the resync tests build on
// in Phase 1.
public sealed class InMemoryAgentTransportTests
{
    [Fact]
    public async Task Controller_message_reaches_the_agent_side()
    {
        InMemoryAgentTransport transport = new InMemoryAgentTransport();
        ControllerMessage sent = new ControllerMessage
        {
            Envelope = new Envelope { ProtocolMajor = 1, ProtocolMinor = 0, MessageId = "m1" },
            HelloAck = new HelloAck { NegotiatedMinor = 0, SessionId = "s1", WantsResync = true }
        };

        await transport.PushToAgentAsync(sent, TestContext.Current.CancellationToken);
        transport.DropStream(); // completes the channel so the enumeration terminates

        List<ControllerMessage> received = new List<ControllerMessage>();
        await foreach (ControllerMessage msg in transport.Incoming(TestContext.Current.CancellationToken))
        {
            received.Add(msg);
        }

        received.Should().ContainSingle();
        received[0].HelloAck.SessionId.Should().Be("s1");
        received[0].HelloAck.WantsResync.Should().BeTrue();
    }

    [Fact]
    public async Task Agent_output_is_observable_by_the_test()
    {
        InMemoryAgentTransport transport = new InMemoryAgentTransport();
        AgentMessage hello = new AgentMessage
        {
            Envelope = new Envelope { ProtocolMajor = 1, ProtocolMinor = 0, MessageId = "a1" },
            Hello = new Hello { AgentId = "agent-1", AgentVersion = "0.0.0", OsFamily = "linux", Arch = "x64" }
        };

        await transport.SendAsync(hello, TestContext.Current.CancellationToken);
        transport.DropStream();

        List<AgentMessage> output = new List<AgentMessage>();
        await foreach (AgentMessage msg in transport.ReadAgentOutput(TestContext.Current.CancellationToken))
        {
            output.Add(msg);
        }

        output.Should().ContainSingle();
        output[0].Hello.AgentId.Should().Be("agent-1");
    }
}
