using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using ServerCenter.Contracts.V1;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Primitives;
using Xunit;

namespace ServerCenter.Controller.Tests;

public sealed class FleetSnapshotBuilderTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero));
    private readonly LibvirtDomainStates _domains = new();
    private TempDatabase _db = null!;
    private AgentNodeRepository _nodes = null!;
    private AgentPresenceStore _presence = null!;
    private FleetSnapshotBuilder _builder = null!;

    public async ValueTask InitializeAsync()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _db = await TempDatabase.CreateAsync(ct);
        _nodes = new AgentNodeRepository(_db.Database);
        _presence = new AgentPresenceStore();
        _builder = new FleetSnapshotBuilder(
            _nodes, _presence, _domains,
            new ServerCenter.Core.Connection.LivenessTracker(staleAfterMs: 30_000, offlineAfterMs: 90_000),
            _clock);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Theory]
    [InlineData(0, AgentLiveness.Online)]       // heartbeat just now
    [InlineData(60_000, AgentLiveness.Stale)]   // 60s ago (>= 30s, < 90s)
    [InlineData(120_000, AgentLiveness.Offline)] // 120s ago (>= 90s)
    public async Task Agent_liveness_reflects_the_heartbeat_gap(long gapMs, AgentLiveness expected)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        long now = _clock.GetUtcNow().ToUnixTimeMilliseconds();
        await SeedNodeAsync("a1", "n1", "host", ct);
        await _presence.OnHeartbeatAsync("a1", new Heartbeat { AgentUnixMs = now - gapMs }, ct);
        await _presence.OnStatusAsync("a1", new NodeStatus { AgentHealth = ServiceHealth.Active }, ct);

        FleetSnapshot snapshot = await _builder.BuildAsync(ct);

        NodeState node = snapshot.Nodes.Should().ContainSingle().Subject;
        node.AgentLiveness.Should().Be(expected);
        node.VmState.Should().Be(VmState.Unknown); // dual-truth: no libvirt yet
        node.Kind.Should().Be("host");
        node.AgentHealth.Should().Be(ServiceHealth.Active);
    }

    [Fact]
    public async Task A_node_with_no_presence_is_offline()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SeedNodeAsync("a2", "n2", "guest", ct);

        FleetSnapshot snapshot = await _builder.BuildAsync(ct);

        snapshot.Nodes.Should().ContainSingle()
            .Which.AgentLiveness.Should().Be(AgentLiveness.Offline);
    }

    [Theory]
    [InlineData(DomainState.Running, VmState.Running)]
    [InlineData(DomainState.ShutOff, VmState.Stopped)]
    [InlineData(DomainState.Crashed, VmState.Stopped)]
    public async Task Vm_state_reflects_the_linked_domain(DomainState domainState, VmState expected)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SeedNodeAsync("a3", "n3", "guest", ct);
        await _nodes.SetLibvirtDomainAsync("n3", "cs2-ffa", ct);
        _domains.Set("cs2-ffa", domainState);

        FleetSnapshot snapshot = await _builder.BuildAsync(ct);

        snapshot.Nodes.Should().ContainSingle().Which.VmState.Should().Be(expected);
    }

    [Fact]
    public async Task A_linked_domain_libvirt_has_not_reported_is_unknown()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SeedNodeAsync("a4", "n4", "guest", ct);
        await _nodes.SetLibvirtDomainAsync("n4", "unseen-vm", ct); // linked, but no state in the cache

        FleetSnapshot snapshot = await _builder.BuildAsync(ct);

        snapshot.Nodes.Should().ContainSingle().Which.VmState.Should().Be(VmState.Unknown);
    }

    private async Task SeedNodeAsync(string agentId, string nodeId, string kind, CancellationToken ct)
    {
        await _nodes.EnsureAgentAsync(agentId, $"name-{nodeId}", "fpr", 1, ct);
        await _nodes.EnsureNodeAsync(nodeId, agentId, kind, "managed", 1, ct);
    }
}
