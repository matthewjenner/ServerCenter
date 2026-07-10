using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using ServerCenter.Capabilities;
using ServerCenter.Core.Capabilities;
using ServerCenter.Core.Games;
using ServerCenter.Core.Primitives;
using ServerCenter.Primitives.Rcon;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Capabilities.Tests;

// The RCON-backed capabilities (stats, graceful shutdown) against the scripted fake RCON server.
public sealed class RconCapabilityTests
{
    private static readonly Dictionary<string, string> InstanceParams = new()
    {
        ["ports.rcon"] = "27016",
        ["rcon.password"] = "secret"
    };

    [Fact]
    public void RconEndpoints_resolves_from_instance_params_with_loopback_default()
    {
        RconEndpoint endpoint = RconEndpoints.From(InstanceParams);

        endpoint.Should().Be(new RconEndpoint("127.0.0.1", 27016, "secret"));
    }

    [Fact]
    public void RconEndpoints_fails_loudly_on_a_missing_port()
    {
        Func<RconEndpoint> act = () => RconEndpoints.From(new Dictionary<string, string> { ["rcon.password"] = "secret" });

        act.Should().Throw<InvalidOperationException>().WithMessage("*ports.rcon*");
    }

    [Fact]
    public async Task Stats_runs_each_command_and_returns_the_raw_responses()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeRconChannelFactory factory = new FakeRconChannelFactory("secret",
            new Dictionary<string, string[]> { ["status"] = ["players 3/10"], ["stats"] = ["cpu 12%"] });
        StatsSpec spec = new StatsSpec("rcon", new Dictionary<string, string> { ["players"] = "status", ["perf"] = "stats" });

        ServerStats result = await new RconStatsCapability(spec, new SourceRconClient(factory))
            .ReadAsync(new StatsContext(InstanceParams), ct);

        result.Raw["players"].Should().Be("players 3/10");
        result.Raw["perf"].Should().Be("cpu 12%");
    }

    [Fact]
    public async Task Shutdown_sends_the_drain_command()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeRconChannelFactory factory = new FakeRconChannelFactory("secret");
        ShutdownSpec spec = new ShutdownSpec("rcon", "say Restarting; sv_shutdown", GraceSeconds: 0);

        await new RconShutdownCapability(spec, new SourceRconClient(factory), TimeProvider.System)
            .GracefulShutdownAsync(new ShutdownContext(0, InstanceParams), new RecordingJobSink(), ct);

        factory.Last!.Sent.Should().ContainSingle(p =>
            p.Type == RconPacketTypes.ExecCommand && p.Body == "say Restarting; sv_shutdown");
    }

    [Fact]
    public async Task Shutdown_waits_out_the_grace_period()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeTimeProvider clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero));
        FakeRconChannelFactory factory = new FakeRconChannelFactory("secret");
        ShutdownSpec spec = new ShutdownSpec("rcon", "sv_shutdown", GraceSeconds: 60);

        Task task = new RconShutdownCapability(spec, new SourceRconClient(factory), clock)
            .GracefulShutdownAsync(new ShutdownContext(0, InstanceParams), new RecordingJobSink(), ct);

        task.IsCompleted.Should().BeFalse(); // still draining/waiting
        clock.Advance(TimeSpan.FromSeconds(60));
        await task; // grace elapsed -> completes
    }
}
