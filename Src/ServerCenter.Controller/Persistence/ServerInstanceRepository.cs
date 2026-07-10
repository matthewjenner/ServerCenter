using Microsoft.Data.Sqlite;
using ServerCenter.Core.Games;

namespace ServerCenter.Controller.Persistence;

// Persists concrete server instances (the instance side of class-vs-instance). instance_params_json
// is stored opaquely; it holds secrets (rcon passwords) and is precious controller state - never
// backed up on the agent of record (brief 8.4).
public sealed class ServerInstanceRepository(ServerCenterDatabase database)
{
    private const string Columns =
        "id, node_id, descriptor_id, descriptor_version, recipe_id, recipe_version, " +
        "policy_id, policy_version, instance_params_json, created_at";

    public async Task InsertAsync(ServerInstance instance, CancellationToken ct)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(ct);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText =
            $"INSERT INTO server_instance ({Columns}) VALUES " +
            "(@id, @node, @did, @dver, @rid, @rver, @pid, @pver, @params, @created);";
        cmd.Parameters.AddWithValue("@id", instance.Id);
        cmd.Parameters.AddWithValue("@node", instance.NodeId);
        cmd.Parameters.AddWithValue("@did", (object?)instance.DescriptorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dver", (object?)instance.DescriptorVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rid", (object?)instance.RecipeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rver", (object?)instance.RecipeVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pid", (object?)instance.PolicyId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pver", (object?)instance.PolicyVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@params", instance.InstanceParamsJson);
        cmd.Parameters.AddWithValue("@created", instance.CreatedAtUnixMs);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<ServerInstance?> GetAsync(string id, CancellationToken ct)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(ct);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM server_instance WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<ServerInstance>> ListByNodeAsync(string nodeId, CancellationToken ct)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(ct);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM server_instance WHERE node_id = @node ORDER BY created_at;";
        cmd.Parameters.AddWithValue("@node", nodeId);

        List<ServerInstance> instances = new List<ServerInstance>();
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            instances.Add(Map(reader));
        }

        return instances;
    }

    private static ServerInstance Map(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        NodeId = r.GetString(1),
        DescriptorId = r.IsDBNull(2) ? null : r.GetString(2),
        DescriptorVersion = r.IsDBNull(3) ? null : r.GetInt32(3),
        RecipeId = r.IsDBNull(4) ? null : r.GetString(4),
        RecipeVersion = r.IsDBNull(5) ? null : r.GetInt32(5),
        PolicyId = r.IsDBNull(6) ? null : r.GetString(6),
        PolicyVersion = r.IsDBNull(7) ? null : r.GetInt32(7),
        InstanceParamsJson = r.GetString(8),
        CreatedAtUnixMs = r.GetInt64(9)
    };
}
