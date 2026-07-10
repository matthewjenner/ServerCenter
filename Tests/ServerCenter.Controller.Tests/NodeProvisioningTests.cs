using AwesomeAssertions;
using ServerCenter.Controller.Persistence;
using Xunit;

namespace ServerCenter.Controller.Tests;

// The provisioning -> managed handoff (brief 3.13): a node is recorded 'provisioning' with its
// libvirt domain, then flips to 'managed' + adopts its agent on first check-in.
public sealed class NodeProvisioningTests : IAsyncLifetime
{
    private TempDatabase _db = null!;
    private AgentNodeRepository _nodes = null!;

    public async ValueTask InitializeAsync()
    {
        _db = await TempDatabase.CreateAsync(TestContext.Current.CancellationToken);
        _nodes = new AgentNodeRepository(_db.Database);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Provision_records_a_provisioning_node_with_its_domain()
    {
        var ct = TestContext.Current.CancellationToken;
        await _nodes.ProvisionNodeAsync("vm-1", "guest", "cs2-ffa", "linux", 1000, ct);

        var node = await _nodes.GetNodeAsync("vm-1", ct);
        node!.Lifecycle.Should().Be("provisioning");
        node.LibvirtDomain.Should().Be("cs2-ffa");
        node.AgentId.Should().BeEmpty(); // no agent yet
    }

    [Fact]
    public async Task Check_in_flips_provisioning_to_managed_and_adopts_the_agent()
    {
        var ct = TestContext.Current.CancellationToken;
        await _nodes.ProvisionNodeAsync("vm-1", "guest", "cs2-ffa", "linux", 1000, ct);
        await _nodes.EnsureAgentAsync("vm-1", "vm-1", "fpr", 1, ct); // the agent enrolls / checks in

        await _nodes.MarkManagedOnCheckInAsync("vm-1", "vm-1", ct);

        var node = await _nodes.GetNodeAsync("vm-1", ct);
        node!.Lifecycle.Should().Be("managed");
        node.AgentId.Should().Be("vm-1");
        node.LibvirtDomain.Should().Be("cs2-ffa"); // preserved through the handoff
    }

    [Fact]
    public async Task Check_in_is_a_no_op_for_an_already_managed_node()
    {
        var ct = TestContext.Current.CancellationToken;
        await _nodes.EnsureAgentAsync("a1", "a1", "fpr", 1, ct);
        await _nodes.EnsureNodeAsync("a1", "a1", "guest", "managed", 1, ct);

        await _nodes.MarkManagedOnCheckInAsync("a1", "a1", ct);

        (await _nodes.GetNodeAsync("a1", ct))!.Lifecycle.Should().Be("managed");
    }
}
