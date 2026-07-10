using AwesomeAssertions;
using ServerCenter.Contracts.V1;
using ServerCenter.Core.Connection;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Transport;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Core.Tests;

// Runs the real AgentHandshake and ControllerHandshake against each other over the in-memory
// duplex link: the Tier 1 proof that the wire, handshake, and resync round-trip correctly.
public sealed class HandshakeEndToEndTests
{
    private static readonly AgentIdentity Identity = new("agent-1", "0.1.0", "linux", "x64");

    [Fact]
    public async Task Empty_resync_round_trips_and_establishes_the_session()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using InMemoryDuplexLink link = new InMemoryDuplexLink();
        FakeControllerJobView jobs = new FakeControllerJobView(); // no open jobs
        FakeAgentJobStateSource agentJobs = new FakeAgentJobStateSource(); // no in-flight jobs

        Task<ControllerHandshakeResult> controllerTask = ControllerHandshake.PerformAsync(link.ControllerSide, jobs, () => "sess-1", ct);
        Task<AgentHandshakeResult> agentTask = AgentHandshake.PerformAsync(link.AgentSide, Identity, agentJobs, ct);
        await Task.WhenAll(controllerTask, agentTask);

        ControllerHandshakeResult controller = await controllerTask;
        AgentHandshakeResult agent = await agentTask;

        controller.Established.Should().BeTrue();
        controller.AgentId.Should().Be("agent-1");
        controller.ReconcileActions.Should().BeEmpty();
        agent.Established.Should().BeTrue();
        agent.SessionId.Should().Be("sess-1");
    }

    [Fact]
    public async Task Node_kind_host_flows_from_the_agent_to_the_controller()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using InMemoryDuplexLink link = new InMemoryDuplexLink();
        AgentIdentity hostIdentity = new AgentIdentity("host-agent", "0.1.0", "linux", "x64", NodeKind: "host");

        Task<ControllerHandshakeResult> controllerTask = ControllerHandshake.PerformAsync(link.ControllerSide, new FakeControllerJobView(), () => "s", ct);
        Task<AgentHandshakeResult> agentTask = AgentHandshake.PerformAsync(link.AgentSide, hostIdentity, new FakeAgentJobStateSource(), ct);
        await Task.WhenAll(controllerTask, agentTask);

        (await controllerTask).NodeKind.Should().Be("host");
    }

    [Fact]
    public async Task Resync_after_reconnect_reconciles_an_in_flight_job()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using InMemoryDuplexLink link = new InMemoryDuplexLink();

        // The controller still believes job-A is running; the agent, on reconnect, reports it
        // finished while the stream was down.
        FakeControllerJobView jobs = new FakeControllerJobView();
        jobs.SeedOpenJobs("agent-1", new ControllerOpenJob("job-A", Requeueable: false));
        FakeAgentJobStateSource agentJobs = new FakeAgentJobStateSource(
            new AgentResyncEntry("job-A", AgentJobLocalState.FinishedSucceeded, 42));

        Task<ControllerHandshakeResult> controllerTask = ControllerHandshake.PerformAsync(link.ControllerSide, jobs, () => "sess-2", ct);
        Task<AgentHandshakeResult> agentTask = AgentHandshake.PerformAsync(link.AgentSide, Identity, agentJobs, ct);
        await Task.WhenAll(controllerTask, agentTask);

        ControllerHandshakeResult controller = await controllerTask;

        controller.Established.Should().BeTrue();
        controller.ReconcileActions.Should().ContainSingle()
            .Which.Should().Be(new ReconcileAction("job-A", ReconcileOutcome.CloseSucceeded));
        jobs.Applied.Should().ContainSingle()
            .Which.Should().Be(new ReconcileAction("job-A", ReconcileOutcome.CloseSucceeded));
    }

    [Fact]
    public async Task Controller_rejects_a_version_skewed_hello_with_goodbye()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using InMemoryDuplexLink link = new InMemoryDuplexLink();
        FakeControllerJobView jobs = new FakeControllerJobView();

        // Push a Hello whose envelope declares an incompatible major, then run the controller.
        AgentMessage badHello = new AgentMessage
        {
            Envelope = new Envelope
            {
                ProtocolMajor = ProtocolVersion.Major + 1,
                ProtocolMinor = 0,
                MessageId = "m1"
            },
            Hello = new Hello { AgentId = "agent-1", AgentVersion = "0.1.0", OsFamily = "linux", Arch = "x64" }
        };
        await link.AgentSide.SendAsync(badHello, ct);

        ControllerHandshakeResult result = await ControllerHandshake.PerformAsync(link.ControllerSide, jobs, () => "unused", ct);

        result.Established.Should().BeFalse();

        // The agent side should observe a typed Goodbye(VERSION_MISMATCH).
        await using IAsyncEnumerator<ControllerMessage> incoming = link.AgentSide.Incoming(ct).GetAsyncEnumerator(ct);
        (await incoming.MoveNextAsync()).Should().BeTrue();
        incoming.Current.PayloadCase.Should().Be(ControllerMessage.PayloadOneofCase.Goodbye);
        incoming.Current.Goodbye.Reason.Should().Be(GoodbyeReason.VersionMismatch);
    }
}
