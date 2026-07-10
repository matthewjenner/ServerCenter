using System.Text.Json;
using ServerCenter.Core.Games;

namespace ServerCenter.Core.Recipes;

// Serializes a stored BuildRecipe (build_recipe.body_json) in the game dialect (camelCase, lowercase
// enum tokens like "kv", null fields omitted, tokens preserved) - the recipe reuses the descriptor's
// ConfigFileSpec/SteamAppSpec, so it shares GameDescriptorSerializer.Options.
public static class BuildRecipeSerializer
{
    public static string Serialize(BuildRecipe recipe) =>
        JsonSerializer.Serialize(recipe, GameDescriptorSerializer.Options);

    public static BuildRecipe Deserialize(string json) =>
        JsonSerializer.Deserialize<BuildRecipe>(json, GameDescriptorSerializer.Options)
        ?? throw new JsonException("build recipe body deserialized to null");
}
