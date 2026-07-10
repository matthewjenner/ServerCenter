using ServerCenter.Core.Updates;

namespace ServerCenter.Controller.Persistence;

// Stores UpdatePolicy classes as versioned JSON (brief 3.11): one (id, version) row per policy
// revision, never mutated in place, so the exact policy that governed a run is always
// reconstructable. The body is validated by round-tripping through UpdatePolicySerializer on write.
public sealed class UpdatePolicyRepository(ServerCenterDatabase database)
{
    // Inserts a new policy version. The body is re-serialized from the model so what lands in the
    // column is canonical, not whatever the caller happened to pass. (id, version) is the primary
    // key, so re-inserting an existing version is a conflict, not a silent overwrite - policy
    // revisions are immutable.
    public async Task InsertAsync(UpdatePolicy policy, long createdAtUnixMs, CancellationToken ct)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO update_policy (id, version, body_json, created_at) " +
            "VALUES (@id, @version, @body, @created);";
        cmd.Parameters.AddWithValue("@id", policy.Id);
        cmd.Parameters.AddWithValue("@version", policy.Version);
        cmd.Parameters.AddWithValue("@body", UpdatePolicySerializer.Serialize(policy));
        cmd.Parameters.AddWithValue("@created", createdAtUnixMs);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<UpdatePolicy?> GetAsync(string id, int version, CancellationToken ct)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT body_json FROM update_policy WHERE id = @id AND version = @version;";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@version", version);
        var body = (string?)await cmd.ExecuteScalarAsync(ct);
        return body is null ? null : UpdatePolicySerializer.Deserialize(body);
    }

    // The newest revision of a policy id (highest version), or null if the id is unknown.
    public async Task<UpdatePolicy?> GetLatestAsync(string id, CancellationToken ct)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT body_json FROM update_policy WHERE id = @id ORDER BY version DESC LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", id);
        var body = (string?)await cmd.ExecuteScalarAsync(ct);
        return body is null ? null : UpdatePolicySerializer.Deserialize(body);
    }
}
