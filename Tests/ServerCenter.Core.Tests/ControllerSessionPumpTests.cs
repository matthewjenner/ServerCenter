using AwesomeAssertions;
using ServerCenter.Contracts.V1;
using ServerCenter.Core.Connection;
using ServerCenter.Core.Transport;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Core.Tests;

public sealed class ControllerSessionPumpTests
{
    [Fact]
    public async Task Ingests_heartbeat_and_status_to_the_sink_tagged_with_the_agent()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using InMemoryDuplexLink link = new InMemoryDuplexLink();
        RecordingControllerSink sink = new RecordingControllerSink();

        Task pump = ControllerSessionPump.RunAsync(link.ControllerSide, "agent-1", sink, ct);

        await link.AgentSide.SendAsync(
            new AgentMessage { Envelope = Envelopes.New(), Heartbeat = new Heartbeat { AgentUnixMs = 123 } }, ct);
        await link.AgentSide.SendAsync(
            new AgentMessage { Envelope = Envelopes.New(), Status = new NodeStatus { AgentHealth = ServiceHealth.Active } }, ct);

        link.DropStream();
        await pump;

        sink.Heartbeats.Should().ContainSingle().Which.AgentId.Should().Be("agent-1");
        sink.Statuses.Should().ContainSingle().Which.Status.AgentHealth.Should().Be(ServiceHealth.Active);
    }
}
