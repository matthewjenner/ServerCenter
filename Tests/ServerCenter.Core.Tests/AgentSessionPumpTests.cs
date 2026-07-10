using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using ServerCenter.Contracts.V1;
using ServerCenter.Core.Connection;
using ServerCenter.Core.Transport;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Core.Tests;

public sealed class AgentSessionPumpTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Sends_heartbeat_and_status_immediately_then_each_interval()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeTimeProvider clock = new FakeTimeProvider();
        using InMemoryDuplexLink link = new InMemoryDuplexLink();

        Task<AgentSessionOutcome> pump = AgentSessionPump.RunAsync(
            link.AgentSide, new FakeAgentStatusSource(), new NoopAgentCommandHandler(), clock, Interval, ct);

        await using IAsyncEnumerator<AgentMessage> incoming = link.ControllerSide.Incoming(ct).GetAsyncEnumerator(ct);

        // Immediate first tick.
        await ExpectAsync(incoming, AgentMessage.PayloadOneofCase.Heartbeat);
        await ExpectAsync(incoming, AgentMessage.PayloadOneofCase.Status);

        // Next interval.
        clock.Advance(Interval);
        await ExpectAsync(incoming, AgentMessage.PayloadOneofCase.Heartbeat);
        await ExpectAsync(incoming, AgentMessage.PayloadOneofCase.Status);

        link.DropStream();
        (await pump).Kind.Should().Be(SessionEndKind.StreamEnded);
    }

    [Fact]
    public async Task Controller_goodbye_ends_the_session_with_that_reason()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using InMemoryDuplexLink link = new InMemoryDuplexLink();

        Task<AgentSessionOutcome> pump = AgentSessionPump.RunAsync(
            link.AgentSide, new FakeAgentStatusSource(), new NoopAgentCommandHandler(),
            new FakeTimeProvider(), Interval, ct);

        await link.ControllerSide.SendAsync(
            new ControllerMessage { Envelope = Envelopes.New(), Goodbye = new Goodbye { Reason = GoodbyeReason.ShuttingDown } },
            ct);

        AgentSessionOutcome outcome = await pump;
        outcome.Kind.Should().Be(SessionEndKind.ControllerGoodbye);
        outcome.GoodbyeReason.Should().Be(GoodbyeReason.ShuttingDown);
    }

    [Fact]
    public async Task Dispatches_pushed_commands_to_the_handler()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using InMemoryDuplexLink link = new InMemoryDuplexLink();
        NoopAgentCommandHandler commands = new NoopAgentCommandHandler();

        Task<AgentSessionOutcome> pump = AgentSessionPump.RunAsync(
            link.AgentSide, new FakeAgentStatusSource(), commands, new FakeTimeProvider(), Interval, ct);

        await link.ControllerSide.SendAsync(
            new ControllerMessage
            {
                Envelope = Envelopes.New(),
                Command = new Command { JobId = "j1", Type = "service.restart", ParamsJson = "{}", Cancellable = false }
            },
            ct);

        // Dropping the stream after the command is queued lets the read loop drain it, then end.
        link.DropStream();
        await pump;

        commands.Commands.Should().ContainSingle().Which.JobId.Should().Be("j1");
    }

    private static async Task ExpectAsync(
        IAsyncEnumerator<AgentMessage> incoming,
        AgentMessage.PayloadOneofCase expected)
    {
        (await incoming.MoveNextAsync()).Should().BeTrue();
        incoming.Current.PayloadCase.Should().Be(expected);
    }
}
