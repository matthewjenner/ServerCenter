using AwesomeAssertions;
using ServerCenter.Core.Connection;
using Xunit;

namespace ServerCenter.Core.Tests;

public sealed class LivenessTrackerTests
{
    private static readonly LivenessTracker Tracker = new(staleAfterMs: 30_000, offlineAfterMs: 90_000);

    [Theory]
    [InlineData(0, AgentLiveness.Online)]
    [InlineData(29_999, AgentLiveness.Online)]
    [InlineData(30_000, AgentLiveness.Stale)]
    [InlineData(89_999, AgentLiveness.Stale)]
    [InlineData(90_000, AgentLiveness.Offline)]
    [InlineData(600_000, AgentLiveness.Offline)]
    public void Evaluate_maps_heartbeat_gap_to_liveness(long gapMs, AgentLiveness expected) =>
        Tracker.Evaluate(lastHeartbeatUnixMs: 1_000_000, nowUnixMs: 1_000_000 + gapMs)
            .Should().Be(expected);

    [Fact]
    public void Ctor_rejects_nonpositive_stale() =>
        ((Action)(() => _ = new LivenessTracker(0, 10))).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void Ctor_rejects_offline_not_exceeding_stale() =>
        ((Action)(() => _ = new LivenessTracker(30_000, 30_000))).Should().Throw<ArgumentOutOfRangeException>();
}
