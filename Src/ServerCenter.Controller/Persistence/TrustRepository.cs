using Microsoft.Data.Sqlite;
using ServerCenter.Controller.Crypto;

namespace ServerCenter.Controller.Persistence;

// Persistence for the trust core: the singleton CA, per-agent identities (fingerprint + status),
// and one-time bootstrap tokens. All precious state (brief 3.8/3.9).
public sealed class TrustRepository(ServerCenterDatabase database)
{
    // ---- CA (singleton row id = 1) ----

    public async Task<CaMaterial?> GetCaAsync(CancellationToken ct)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(ct);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT cert_pem, key_pem FROM controller_ca WHERE id = 1;";
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new CaMaterial(reader.GetString(0), reader.GetString(1)) : null;
    }

    public async Task SaveCaAsync(CaMaterial ca, long createdAtUnixMs, CancellationToken ct)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(ct);
        await using SqliteCommand cmd = connection.CreateCommand();
        // First writer wins under a race; a CA already present is never overwritten.
        cmd.CommandText =
            "INSERT INTO controller_ca (id, cert_pem, key_pem, created_at) " +
            "VALUES (1, @cert, @key, @created) ON CONFLICT(id) DO NOTHING;";
        cmd.Parameters.AddWithValue("@cert", ca.CertPem);
        cmd.Parameters.AddWithValue("@key", ca.KeyPem);
        cmd.Parameters.AddWithValue("@created", createdAtUnixMs);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ---- Agent identities ----

    public async Task InsertIdentityAsync(
        string id, string displayName, string certFingerprint, string status, long enrolledAtUnixMs, CancellationToken ct)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(ct);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO agent_identity (id, display_name, cert_fpr, status, enrolled_at) " +
            "VALUES (@id, @name, @fpr, @status, @enrolled);";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@name", displayName);
        cmd.Parameters.AddWithValue("@fpr", certFingerprint);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@enrolled", enrolledAtUnixMs);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<AgentIdentityRow?> GetIdentityAsync(string id, CancellationToken ct)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(ct);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, display_name, cert_fpr, status FROM agent_identity WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new AgentIdentityRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3))
            : null;
    }

    public async Task SetStatusAsync(string id, string status, long? revokedAtUnixMs, CancellationToken ct)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(ct);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE agent_identity SET status = @status, revoked_at = @revoked WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@revoked", (object?)revokedAtUnixMs ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetFingerprintAsync(string id, string certFingerprint, long rotatedAtUnixMs, CancellationToken ct)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(ct);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE agent_identity SET cert_fpr = @fpr, rotated_at = @rotated WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@fpr", certFingerprint);
        cmd.Parameters.AddWithValue("@rotated", rotatedAtUnixMs);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ---- Bootstrap tokens ----

    public async Task InsertTokenAsync(string tokenSha256, string displayName, long expiresAtUnixMs, CancellationToken ct)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(ct);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO bootstrap_token (token_sha256, display_name, expires_at) VALUES (@tok, @name, @exp);";
        cmd.Parameters.AddWithValue("@tok", tokenSha256);
        cmd.Parameters.AddWithValue("@name", displayName);
        cmd.Parameters.AddWithValue("@exp", expiresAtUnixMs);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Atomically consumes a token: marks it used and returns its display name, but only if it
    // is unused and unexpired. Returns null otherwise. One-time enforcement lives in the WHERE.
    public async Task<string?> ConsumeTokenAsync(string tokenSha256, long nowUnixMs, CancellationToken ct)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(ct);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText =
            "UPDATE bootstrap_token SET used_at = @now " +
            "WHERE token_sha256 = @tok AND used_at IS NULL AND expires_at > @now " +
            "RETURNING display_name;";
        cmd.Parameters.AddWithValue("@tok", tokenSha256);
        cmd.Parameters.AddWithValue("@now", nowUnixMs);
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? reader.GetString(0) : null;
    }
}

public sealed record AgentIdentityRow(string Id, string DisplayName, string CertFpr, string Status);
