using AwesomeAssertions;
using ServerCenter.Controller.Persistence;
using ServerCenter.Core.Games;
using Xunit;

namespace ServerCenter.Controller.Tests;

// Descriptors persist as immutable versioned rows (real temp SQLite); a stored revision round-trips
// through the model and GetLatest picks the highest version.
public sealed class GameDescriptorRepositoryTests : IAsyncLifetime
{
    private TempDatabase _db = null!;
    private GameDescriptorRepository _descriptors = null!;

    public async ValueTask InitializeAsync()
    {
        _db = await TempDatabase.CreateAsync(TestContext.Current.CancellationToken);
        _descriptors = new GameDescriptorRepository(_db.Database);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    private static GameDescriptor Descriptor(int version) => new()
    {
        Id = "cs2-dedicated",
        Version = version,
        SteamApp = new SteamAppSpec(730, "/opt/cs2"),
        Capabilities = new GameCapabilities
        {
            Stats = new StatsSpec("rcon", new Dictionary<string, string> { ["players"] = "status" })
        }
    };

    [Fact]
    public async Task Insert_and_get_round_trips_the_descriptor()
    {
        var ct = TestContext.Current.CancellationToken;
        await _descriptors.InsertAsync(Descriptor(3), 1000, ct);

        var got = await _descriptors.GetAsync("cs2-dedicated", 3, ct);

        got.Should().BeEquivalentTo(Descriptor(3));
    }

    [Fact]
    public async Task GetLatest_returns_the_highest_version()
    {
        var ct = TestContext.Current.CancellationToken;
        await _descriptors.InsertAsync(Descriptor(1), 1000, ct);
        await _descriptors.InsertAsync(Descriptor(3), 3000, ct);
        await _descriptors.InsertAsync(Descriptor(2), 2000, ct);

        (await _descriptors.GetLatestAsync("cs2-dedicated", ct))!.Version.Should().Be(3);
    }
}
