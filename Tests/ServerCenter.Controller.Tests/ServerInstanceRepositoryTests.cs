using AwesomeAssertions;
using ServerCenter.Controller.Persistence;
using ServerCenter.Core.Games;
using Xunit;

namespace ServerCenter.Controller.Tests;

// A concrete server instance round-trips (real temp SQLite), including its opaque params JSON and
// the pinned descriptor version, and lists by node.
public sealed class ServerInstanceRepositoryTests : IAsyncLifetime
{
    private TempDatabase _db = null!;
    private ServerInstanceRepository _instances = null!;

    public async ValueTask InitializeAsync()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _db = await TempDatabase.CreateAsync(ct);
        _instances = new ServerInstanceRepository(_db.Database);

        AgentNodeRepository nodes = new AgentNodeRepository(_db.Database);
        await nodes.EnsureAgentAsync("agent-1", "agent-1", "fpr", 1, ct);
        await nodes.EnsureNodeAsync("node-1", "agent-1", "guest", "managed", 1, ct);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    private static ServerInstance Instance(string id) => new()
    {
        Id = id,
        NodeId = "node-1",
        DescriptorId = "cs2-dedicated",
        DescriptorVersion = 3,
        InstanceParamsJson = """{"ports":{"game":27015}}""",
        CreatedAtUnixMs = 1000
    };

    [Fact]
    public async Task Insert_and_get_round_trips_all_fields()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await _instances.InsertAsync(Instance("srv-1"), ct);

        ServerInstance? got = await _instances.GetAsync("srv-1", ct);

        got.Should().BeEquivalentTo(Instance("srv-1"));
    }

    [Fact]
    public async Task ListByNode_returns_the_node_instances()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await _instances.InsertAsync(Instance("srv-1"), ct);
        await _instances.InsertAsync(Instance("srv-2"), ct);

        IReadOnlyList<ServerInstance> list = await _instances.ListByNodeAsync("node-1", ct);

        list.Select(i => i.Id).Should().BeEquivalentTo(["srv-1", "srv-2"]);
    }
}
