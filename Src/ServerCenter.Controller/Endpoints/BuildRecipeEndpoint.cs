using ServerCenter.Controller.Persistence;
using ServerCenter.Core.Recipes;

namespace ServerCenter.Controller.Endpoints;

// Operator surface for the declarative build recipes (Phase 7): store a revision and list the latest
// of each. Body is read raw and parsed with BuildRecipeSerializer (canonical dialect), like
// /update-policies - replaces hand-seeding SQLite. Operator auth deferred.
public static class BuildRecipeEndpoint
{
    public static void MapBuildRecipes(this WebApplication app)
    {
        app.MapPost("/build-recipes",
            async (HttpRequest request, BuildRecipeRepository repo, TimeProvider clock, CancellationToken ct) =>
            {
                using StreamReader reader = new StreamReader(request.Body);
                string body = await reader.ReadToEndAsync(ct);
                BuildRecipe recipe = BuildRecipeSerializer.Deserialize(body);
                await repo.InsertAsync(recipe, clock.GetUtcNow().ToUnixTimeMilliseconds(), ct);
                return Results.Ok(new { recipe.Id, recipe.Version });
            });

        app.MapGet("/build-recipes",
            async (BuildRecipeRepository repo, CancellationToken ct) =>
            {
                IReadOnlyList<BuildRecipe> list = await repo.ListLatestAsync(ct);
                string json = "[" + string.Join(",", list.Select(BuildRecipeSerializer.Serialize)) + "]";
                return Results.Text(json, "application/json");
            });
    }
}
