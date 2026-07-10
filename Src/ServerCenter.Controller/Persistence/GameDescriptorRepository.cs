using Microsoft.Data.Sqlite;
using ServerCenter.Core.Games;

namespace ServerCenter.Controller.Persistence;

// Stores game descriptors as versioned JSON (brief 3.10): one immutable (id, version) row per
// revision, validated by round-tripping through GameDescriptorSerializer on write, so the exact
// descriptor that built or governs a server is always reconstructable. Mirrors UpdatePolicyRepository.
public sealed class GameDescriptorRepository(ServerCenterDatabase database)
{
    public async Task InsertAsync(GameDescriptor descriptor, long createdAtUnixMs, CancellationToken ct)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(ct);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO game_descriptor (id, version, body_json, created_at) " +
            "VALUES (@id, @version, @body, @created);";
        cmd.Parameters.AddWithValue("@id", descriptor.Id);
        cmd.Parameters.AddWithValue("@version", descriptor.Version);
        cmd.Parameters.AddWithValue("@body", GameDescriptorSerializer.Serialize(descriptor));
        cmd.Parameters.AddWithValue("@created", createdAtUnixMs);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<GameDescriptor?> GetAsync(string id, int version, CancellationToken ct)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(ct);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT body_json FROM game_descriptor WHERE id = @id AND version = @version;";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@version", version);
        string? body = (string?)await cmd.ExecuteScalarAsync(ct);
        return body is null ? null : GameDescriptorSerializer.Deserialize(body);
    }

    public async Task<GameDescriptor?> GetLatestAsync(string id, CancellationToken ct)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(ct);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT body_json FROM game_descriptor WHERE id = @id ORDER BY version DESC LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", id);
        string? body = (string?)await cmd.ExecuteScalarAsync(ct);
        return body is null ? null : GameDescriptorSerializer.Deserialize(body);
    }
}
