namespace ServerCenter.Controller.Persistence;

// Minimal agent/node persistence. The full identity lifecycle (mint/pin/rotate/revoke) lands
// in the mTLS ship; for now this is enough to register a connecting agent and its node so jobs
// can be keyed to it. Upserts are idempotent (safe to call on every connect).
public sealed class AgentNodeRepository(ServerCenterDatabase database)
{
    public async Task EnsureAgentAsync(
        string agentId, string displayName, string certFingerprint, long enrolledAtUnixMs, CancellationToken ct)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO agent_identity (id, display_name, cert_fpr, status, enrolled_at) " +
            "VALUES (@id, @name, @fpr, 'active', @enrolled) " +
            "ON CONFLICT(id) DO NOTHING;";
        cmd.Parameters.AddWithValue("@id", agentId);
        cmd.Parameters.AddWithValue("@name", displayName);
        cmd.Parameters.AddWithValue("@fpr", certFingerprint);
        cmd.Parameters.AddWithValue("@enrolled", enrolledAtUnixMs);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task EnsureNodeAsync(
        string nodeId, string agentId, string kind, string lifecycle, long createdAtUnixMs, CancellationToken ct)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO node (id, agent_id, kind, lifecycle, created_at) " +
            "VALUES (@id, @agent, @kind, @lifecycle, @created) " +
            "ON CONFLICT(id) DO NOTHING;";
        cmd.Parameters.AddWithValue("@id", nodeId);
        cmd.Parameters.AddWithValue("@agent", agentId);
        cmd.Parameters.AddWithValue("@kind", kind);
        cmd.Parameters.AddWithValue("@lifecycle", lifecycle);
        cmd.Parameters.AddWithValue("@created", createdAtUnixMs);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
