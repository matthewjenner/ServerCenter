using Microsoft.Data.Sqlite;
using ServerCenter.Controller.Persistence;

namespace ServerCenter.Controller.Tests;

// A throwaway SQLite file for a test, initialized with the schema and deleted (with its WAL
// sidecars) on dispose. Pools are cleared first so the file handle is released for deletion.
internal sealed class TempDatabase : IAsyncDisposable
{
    public ServerCenterDatabase Database { get; }

    public string Path { get; }

    private TempDatabase(string path, ServerCenterDatabase database)
    {
        Path = path;
        Database = database;
    }

    public static async Task<TempDatabase> CreateAsync(CancellationToken ct)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sc-test-{Guid.NewGuid():N}.db");
        ServerCenterDatabase database = new ServerCenterDatabase(path);
        await database.InitializeAsync(ct);
        return new TempDatabase(path, database);
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (string? file in new[] { Path, Path + "-wal", Path + "-shm" })
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
                // best effort; the temp directory is cleaned by the OS eventually
            }
        }

        return ValueTask.CompletedTask;
    }
}
