using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using ServerCenter.Controller.Persistence;
using ServerCenter.Core.Jobs;
using Xunit;

namespace ServerCenter.Controller.Tests;

public sealed class PersistenceRestartTests
{
    [Fact]
    public async Task Jobs_survive_a_controller_restart()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sc-restart-{Guid.NewGuid():N}.db");

        try
        {
            // First "process": init, register an agent/node, persist a job.
            ServerCenterDatabase db1 = new ServerCenterDatabase(path);
            await db1.InitializeAsync(ct);
            AgentNodeRepository agents1 = new AgentNodeRepository(db1);
            await agents1.EnsureAgentAsync("agent-1", "agent-1", "fpr", 1, ct);
            await agents1.EnsureNodeAsync("node-1", "agent-1", "guest", "managed", 1, ct);
            await new JobRepository(db1).InsertAsync(new Job
            {
                Id = "j1",
                NodeId = "node-1",
                Type = "service.restart",
                ParamsJson = "{}",
                State = JobState.Running,
                Cancellable = false,
                Requeueable = false,
                CreatedAtUnixMs = 1000
            }, ct);

            // Simulate the process exiting (release file handles).
            SqliteConnection.ClearAllPools();

            // Second "process": re-init (idempotent) against the same file, read the job back.
            ServerCenterDatabase db2 = new ServerCenterDatabase(path);
            await db2.InitializeAsync(ct);
            Job? got = await new JobRepository(db2).GetAsync("j1", ct);

            got.Should().NotBeNull();
            got!.State.Should().Be(JobState.Running);
            got.NodeId.Should().Be("node-1");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string? file in new[] { path, path + "-wal", path + "-shm" })
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }
    }
}
