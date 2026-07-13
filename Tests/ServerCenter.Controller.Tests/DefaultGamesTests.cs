using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Games;
using ServerCenter.Core.Recipes;
using Xunit;

namespace ServerCenter.Controller.Tests;

// The seeded starter games: an operator can create a server without hand-authoring a descriptor/recipe.
public sealed class DefaultGamesTests : IAsyncLifetime
{
    private TempDatabase _db = null!;
    private GameDescriptorRepository _descriptors = null!;
    private BuildRecipeRepository _recipes = null!;

    public async ValueTask InitializeAsync()
    {
        _db = await TempDatabase.CreateAsync(TestContext.Current.CancellationToken);
        _descriptors = new GameDescriptorRepository(_db.Database);
        _recipes = new BuildRecipeRepository(_db.Database);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Seeds_a_cs2_descriptor_and_recipe_with_per_instance_templates()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        await DefaultGames.EnsureAsync(_descriptors, _recipes, new FakeTimeProvider(), ct);

        GameDescriptor descriptor = (await _descriptors.GetLatestAsync("cs2", ct))!;
        descriptor.SteamApp.AppId.Should().Be(730);
        descriptor.SteamApp.InstallDir.Should().Contain("{{instance.id}}");
        descriptor.Capabilities.ConfigGen!.Files[0].Path.Should().Contain("{{instance.dir}}");

        BuildRecipe recipe = (await _recipes.GetLatestAsync("cs2", ct))!;
        recipe.SteamApp!.AppId.Should().Be(730);
        recipe.ServiceDefinition!.Unit.Should().Be("sc-cs2-{{instance.id}}.service");
        recipe.ServiceDefinition!.ExecStart.Should().Contain("cs2.sh");
        recipe.ServiceDefinition!.ExecStart.Should().Contain("{{ports.game}}");
    }

    [Fact]
    public async Task Ensure_is_idempotent()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        await DefaultGames.EnsureAsync(_descriptors, _recipes, new FakeTimeProvider(), ct);
        await DefaultGames.EnsureAsync(_descriptors, _recipes, new FakeTimeProvider(), ct);

        (await _descriptors.ListLatestAsync(ct)).Should().ContainSingle(d => d.Id == "cs2");
        (await _recipes.ListLatestAsync(ct)).Should().ContainSingle(r => r.Id == "cs2");
    }
}
