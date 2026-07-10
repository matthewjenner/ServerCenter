using Microsoft.Data.Sqlite;

namespace ServerCenter.Controller.Persistence;

// The controller's precious state lives here (brief 3.9): jobs, identities, and later the
// descriptors/policies/recipes/instance-params. WAL mode is set once at init; foreign keys are
// per-connection so they are enabled on every open. Schema is applied by user_version
// migrations. Live presence (heartbeat/status) is deliberately NOT here - it is transient.
public sealed class ServerCenterDatabase(string dataSource)
{
    private readonly string _connectionString =
        new SqliteConnectionStringBuilder { DataSource = dataSource }.ToString();

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct)
    {
        SqliteConnection connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        await pragma.ExecuteNonQueryAsync(ct);

        return connection;
    }

    // Ordered migrations, applied by user_version. Each new schema change appends an entry.
    private static readonly (long Version, string Ddl)[] Migrations =
    [
        (1, SchemaV1.Ddl),
        (2, SchemaV2.Ddl),
        (3, SchemaV3.Ddl),
        (4, SchemaV4.Ddl),
        (5, SchemaV5.Ddl)
    ];

    public async Task InitializeAsync(CancellationToken ct)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(ct);

        // WAL is a durable, DB-level setting and cannot run inside a transaction.
        await ExecuteAsync(connection, null, "PRAGMA journal_mode = WAL;", ct);

        long version = Convert.ToInt64(await ScalarAsync(connection, "PRAGMA user_version;", ct));
        foreach ((long target, string? ddl) in Migrations)
        {
            if (version >= target)
            {
                continue;
            }

            await using SqliteTransaction tx = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
            await ExecuteAsync(connection, tx, ddl, ct);
            await ExecuteAsync(connection, tx, $"PRAGMA user_version = {target};", ct);
            await tx.CommitAsync(ct);
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction? tx, string sql, CancellationToken ct)
    {
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql, CancellationToken ct)
    {
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteScalarAsync(ct);
    }
}
