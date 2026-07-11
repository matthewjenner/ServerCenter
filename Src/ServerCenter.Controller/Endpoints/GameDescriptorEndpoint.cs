using ServerCenter.Controller.Persistence;
using ServerCenter.Core.Games;

namespace ServerCenter.Controller.Endpoints;

// Operator surface for the declarative game descriptors (Phase 5): store a revision and list the
// latest of each. The body is read raw and parsed with GameDescriptorSerializer (the canonical
// dialect), exactly like /update-policies - so this replaces hand-seeding SQLite. Operator auth is
// deferred (same posture as the other endpoints).
public static class GameDescriptorEndpoint
{
    public static void MapGameDescriptors(this WebApplication app)
    {
        app.MapPost("/game-descriptors",
            async (HttpRequest request, GameDescriptorRepository repo, TimeProvider clock, CancellationToken ct) =>
            {
                using StreamReader reader = new StreamReader(request.Body);
                string body = await reader.ReadToEndAsync(ct);
                GameDescriptor descriptor = GameDescriptorSerializer.Deserialize(body);
                await repo.InsertAsync(descriptor, clock.GetUtcNow().ToUnixTimeMilliseconds(), ct);
                return Results.Ok(new { descriptor.Id, descriptor.Version });
            });

        app.MapGet("/game-descriptors",
            async (GameDescriptorRepository repo, CancellationToken ct) =>
            {
                IReadOnlyList<GameDescriptor> list = await repo.ListLatestAsync(ct);
                string json = "[" + string.Join(",", list.Select(GameDescriptorSerializer.Serialize)) + "]";
                return Results.Text(json, "application/json");
            });
    }
}
