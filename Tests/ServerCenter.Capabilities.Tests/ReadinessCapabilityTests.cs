using AwesomeAssertions;
using ServerCenter.Capabilities;
using ServerCenter.Core.Capabilities;
using ServerCenter.Core.Games;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Capabilities.Tests;

// Readiness is game-level and token-driven: resolve the port from instance params, probe it, and map
// open -> AcceptingPlayers, closed -> Down. Unimplemented primitives fail loudly.
public sealed class ReadinessCapabilityTests
{
    private static readonly Dictionary<string, string> Params = new() { ["ports.game"] = "27015" };
    private static readonly ReadinessSpec PortProbe = new("port-probe", "{{ports.game}}");

    [Fact]
    public async Task An_open_port_is_accepting_players_and_the_token_resolves()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakePortProbe probe = new FakePortProbe(open: true);

        Readiness readiness = await new ReadinessCapability(PortProbe, probe)
            .ProbeAsync(new ReadinessContext(Params), ct);

        readiness.Should().Be(Readiness.AcceptingPlayers);
        probe.Probes.Should().ContainSingle().Which.Should().Be(("127.0.0.1", 27015));
    }

    [Fact]
    public async Task A_closed_port_is_down()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        Readiness readiness = await new ReadinessCapability(PortProbe, new FakePortProbe(open: false))
            .ProbeAsync(new ReadinessContext(Params), ct);

        readiness.Should().Be(Readiness.Down);
    }

    [Fact]
    public async Task An_unsupported_primitive_fails_loudly()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        ReadinessSpec spec = new ReadinessSpec("query-protocol", "{{ports.game}}", "a2s");

        Func<Task<Readiness>> act = async () =>
            await new ReadinessCapability(spec, new FakePortProbe()).ProbeAsync(new ReadinessContext(Params), ct);

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
