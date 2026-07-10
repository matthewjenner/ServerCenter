using ServerCenter.Core.Recipes;

namespace ServerCenter.Controller.Persistence;

// Stores build recipes as versioned JSON (brief 3.12): immutable (id, version) rows, validated by
// round-tripping through BuildRecipeSerializer on write, so the exact recipe that built a server is
// always reconstructable. Mirrors UpdatePolicyRepository / GameDescriptorRepository.
public sealed class BuildRecipeRepository(ServerCenterDatabase database)
{
    public async Task InsertAsync(BuildRecipe recipe, long createdAtUnixMs, CancellationToken ct)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO build_recipe (id, version, body_json, created_at) " +
            "VALUES (@id, @version, @body, @created);";
        cmd.Parameters.AddWithValue("@id", recipe.Id);
        cmd.Parameters.AddWithValue("@version", recipe.Version);
        cmd.Parameters.AddWithValue("@body", BuildRecipeSerializer.Serialize(recipe));
        cmd.Parameters.AddWithValue("@created", createdAtUnixMs);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<BuildRecipe?> GetAsync(string id, int version, CancellationToken ct)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT body_json FROM build_recipe WHERE id = @id AND version = @version;";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@version", version);
        var body = (string?)await cmd.ExecuteScalarAsync(ct);
        return body is null ? null : BuildRecipeSerializer.Deserialize(body);
    }

    public async Task<BuildRecipe?> GetLatestAsync(string id, CancellationToken ct)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT body_json FROM build_recipe WHERE id = @id ORDER BY version DESC LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", id);
        var body = (string?)await cmd.ExecuteScalarAsync(ct);
        return body is null ? null : BuildRecipeSerializer.Deserialize(body);
    }
}
