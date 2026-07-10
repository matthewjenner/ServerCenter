using AwesomeAssertions;
using ServerCenter.Core.Games;
using ServerCenter.Core.Recipes;
using Xunit;

namespace ServerCenter.Core.Tests;

// The stored recipe body matches the brief's schema (camelCase, lowercase enum tokens, tokens
// preserved) and round-trips losslessly; undeclared sections are omitted and stay null.
public sealed class BuildRecipeSerializerTests
{
    private static readonly BuildRecipe Cs2 = new()
    {
        Id = "cs2-server",
        Version = 5,
        BaseRequirements = new BaseRequirements("apt", ["lib32gcc-s1", "steamcmd"]),
        SteamApp = new SteamAppSpec(730, "/opt/cs2"),
        ConfigFiles = [new ConfigFileSpec("cs2/server.cfg", "/opt/cs2/cfg/server.cfg", ConfigFormat.Kv)],
        Scripts =
        [
            new RecipeScript("workshop", "install-collection.sh", "test -f /opt/cs2/.collection-ok", "touch /opt/cs2/.collection-ok")
        ],
        ServiceDefinition = new ServiceDefinition("cs2.service", "/opt/cs2/start.sh", "cs2"),
        DescriptorRef = new DescriptorRef("cs2-dedicated", 3)
    };

    [Fact]
    public void Serialize_emits_the_brief_schema_tokens()
    {
        var json = BuildRecipeSerializer.Serialize(Cs2);

        json.Should().Contain("\"format\":\"kv\"");
        json.Should().Contain("\"alreadyDone\":\"test -f /opt/cs2/.collection-ok\"");
        json.Should().Contain("\"unit\":\"cs2.service\"");
        json.Should().Contain("\"provider\":\"apt\"");
    }

    [Fact]
    public void Round_trips_losslessly()
    {
        BuildRecipeSerializer.Deserialize(BuildRecipeSerializer.Serialize(Cs2)).Should().BeEquivalentTo(Cs2);
    }

    [Fact]
    public void A_config_only_recipe_omits_the_absent_sections()
    {
        var minimal = new BuildRecipe
        {
            Id = "config-only",
            Version = 1,
            ConfigFiles = [new ConfigFileSpec("x", "/x", ConfigFormat.Ini)]
        };

        var json = BuildRecipeSerializer.Serialize(minimal);
        json.Should().NotContain("steamApp");
        json.Should().NotContain("serviceDefinition");

        BuildRecipeSerializer.Deserialize(json).SteamApp.Should().BeNull();
    }
}
