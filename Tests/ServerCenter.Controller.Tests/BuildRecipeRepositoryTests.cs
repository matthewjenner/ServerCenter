using AwesomeAssertions;
using ServerCenter.Controller.Persistence;
using ServerCenter.Core.Games;
using ServerCenter.Core.Recipes;
using Xunit;

namespace ServerCenter.Controller.Tests;

// Recipes persist as immutable versioned rows (real temp SQLite); round-trip + GetLatest.
public sealed class BuildRecipeRepositoryTests : IAsyncLifetime
{
    private TempDatabase _db = null!;
    private BuildRecipeRepository _recipes = null!;

    public async ValueTask InitializeAsync()
    {
        _db = await TempDatabase.CreateAsync(TestContext.Current.CancellationToken);
        _recipes = new BuildRecipeRepository(_db.Database);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    private static BuildRecipe Recipe(int version) => new()
    {
        Id = "cs2-server",
        Version = version,
        SteamApp = new SteamAppSpec(730, "/opt/cs2"),
        ServiceDefinition = new ServiceDefinition("cs2.service", "/opt/cs2/start.sh")
    };

    [Fact]
    public async Task Insert_and_get_round_trips_the_recipe()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await _recipes.InsertAsync(Recipe(5), 1000, ct);

        (await _recipes.GetAsync("cs2-server", 5, ct)).Should().BeEquivalentTo(Recipe(5));
    }

    [Fact]
    public async Task GetLatest_returns_the_highest_version()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await _recipes.InsertAsync(Recipe(1), 1000, ct);
        await _recipes.InsertAsync(Recipe(5), 5000, ct);
        await _recipes.InsertAsync(Recipe(2), 2000, ct);

        (await _recipes.GetLatestAsync("cs2-server", ct))!.Version.Should().Be(5);
    }
}
