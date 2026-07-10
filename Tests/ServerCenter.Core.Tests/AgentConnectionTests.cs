using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using ServerCenter.Contracts.V1;
using ServerCenter.Core.Connection;
using ServerCenter.Core.Transport;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Core.Tests;

public sealed class AgentConnectionTests
{
    private static readonly AgentIdentity Identity = new("agent-1", "0.1.0", "linux", "x64");

    private static AgentConnectionOptions Options() =>
        new(TimeSpan.FromSeconds(10),
            new BackoffPolicy(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(100), () => 1.0));

    [Fact]
    public async Task Stops_without_reconnect_on_terminal_goodbye()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectCount = 0;

        async Task RejectingControllerAsync(InMemoryDuplexLink link)
        {
            await using var e = link.ControllerSide.Incoming(ct).GetAsyncEnumerator(ct);
            await e.MoveNextAsync(); // the Hello
            await link.ControllerSide.SendAsync(
                new ControllerMessage { Envelope = Envelopes.New(), Goodbye = new Goodbye { Reason = GoodbyeReason.VersionMismatch } },
                ct);
        }

        Task<IAgentTransport> Connect(CancellationToken token)
        {
            _ = token;
            connectCount++;
            var link = new InMemoryDuplexLink();
            _ = RejectingControllerAsync(link);
            return Task.FromResult(link.AgentSide);
        }

        await AgentConnection.RunAsync(
            Connect, Identity, new FakeAgentJobStateSource(), new FakeAgentStatusSource(),
            new NoopAgentCommandHandler(), new FakeTimeProvider(), Options(), ct);

        connectCount.Should().Be(1); // terminal rejection => no retry
    }

    [Fact]
    public async Task Retries_after_a_transient_connect_failure()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var clock = new FakeTimeProvider();
        var connectCount = 0;
        var secondAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<IAgentTransport> Connect(CancellationToken _)
        {
            if (Interlocked.Increment(ref connectCount) >= 2)
            {
                secondAttempt.TrySetResult();
            }

            throw new InvalidOperationException("transient dial failure");
        }

        var run = AgentConnection.RunAsync(
            Connect, Identity, new FakeAgentJobStateSource(), new FakeAgentStatusSource(),
            new NoopAgentCommandHandler(), clock, Options(), cts.Token);

        // Elapse the backoff on the fake clock so the retry fires.
        for (var i = 0; i < 200 && !secondAttempt.Task.IsCompleted; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(5, cts.Token);
        }

        await secondAttempt.Task;
        connectCount.Should().BeGreaterThanOrEqualTo(2);

        await cts.CancelAsync();
        try
        {
            await run;
        }
        catch (OperationCanceledException)
        {
            // expected on cancellation
        }
    }
}
