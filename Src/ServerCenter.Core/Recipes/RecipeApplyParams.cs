using System.Text.Json;
using ServerCenter.Core.Games;

namespace ServerCenter.Core.Recipes;

// The params the controller pushes for a recipe.apply job: the resolved recipe plus the instance's
// flattened params and the config templates shipped inline (same mechanism as server.config-apply -
// the controller owns the recipe + templates, the agent does the convergent work). Serialized in the
// game dialect (shared by recipe/descriptor).
public sealed record RecipeApplyParams(
    BuildRecipe Recipe,
    IReadOnlyDictionary<string, string> InstanceParams,
    IReadOnlyDictionary<string, string> Templates);

public static class RecipeApplyParamsSerializer
{
    public static string Serialize(RecipeApplyParams request) =>
        JsonSerializer.Serialize(request, GameDescriptorSerializer.Options);

    public static RecipeApplyParams Deserialize(string json) =>
        JsonSerializer.Deserialize<RecipeApplyParams>(json, GameDescriptorSerializer.Options)
        ?? throw new JsonException("recipe.apply params deserialized to null");
}
