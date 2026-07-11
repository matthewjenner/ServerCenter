using System.Text.Json;
using Microsoft.Data.Sqlite;
using ServerCenter.Controller.Persistence;
using ServerCenter.Core.Games;

namespace ServerCenter.Controller.Endpoints;

// Operator surface for server instances (the instance side of class-vs-instance, Phase 5/7): store an
// instance and list them all. Unlike descriptors/recipes there is no enum dialect, so the body is
// plain JSON matching the ServerInstance shape (instanceParamsJson is an opaque string that holds the
// per-instance params + secrets). created_at is stamped server-side. Replaces hand-seeding SQLite.
public static class ServerInstanceEndpoint
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapServerInstances(this WebApplication app)
    {
        app.MapPost("/server-instances",
            async (HttpRequest request, ServerInstanceRepository repo, TimeProvider clock, CancellationToken ct) =>
            {
                using StreamReader reader = new StreamReader(request.Body);
                string body = await reader.ReadToEndAsync(ct);

                ServerInstance? parsed;
                try
                {
                    parsed = JsonSerializer.Deserialize<ServerInstance>(body, Json);
                }
                catch (JsonException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }

                if (parsed is null)
                {
                    return Results.BadRequest(new { error = "empty server instance body" });
                }

                ServerInstance instance = parsed with { CreatedAtUnixMs = clock.GetUtcNow().ToUnixTimeMilliseconds() };
                try
                {
                    await repo.InsertAsync(instance, ct);
                }
                catch (SqliteException ex)
                {
                    // e.g. unknown nodeId (FK), or a duplicate id - an operator error, not a 500.
                    return Results.BadRequest(new { error = ex.Message });
                }

                return Results.Ok(new { instance.Id, instance.NodeId });
            });

        app.MapGet("/server-instances",
            async (ServerInstanceRepository repo, CancellationToken ct) =>
                Results.Json(await repo.ListAllAsync(ct), Json));
    }
}
