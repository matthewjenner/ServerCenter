using System.Text.Json;
using ServerCenter.Core.Capabilities;
using ServerCenter.Core.Games;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Platform;

namespace ServerCenter.Agent.Jobs;

// Executes a "server.remove" job: tears down one server instance's footprint so N-of-a-game can be
// added and removed independently. Order: stop + disable + delete its systemd unit (best-effort - a
// not-running or already-removed unit must not block cleanup) -> daemon-reload -> delete the install
// dir -> delete each config file. Idempotent: re-running on an already-clean box succeeds. The unit,
// install dir, and config paths are already rendered per-instance controller-side.
public sealed class ServerRemoveExecutor(IServiceController services, IPathCleaner cleaner) : IJobExecutor
{
    public string JobType => "server.remove";

    public async Task<JobOutcome> ExecuteAsync(JobContext context, IJobSink sink, CancellationToken ct)
    {
        ServerRemoveParams request;
        try
        {
            request = ServerJobParamsSerializer.Deserialize<ServerRemoveParams>(context.ParamsJson);
        }
        catch (JsonException ex)
        {
            return JobOutcome.Failure($"invalid server.remove params: {ex.Message}");
        }

        if (!string.IsNullOrWhiteSpace(request.Unit))
        {
            sink.Log(LogStream.Note, $"stopping + removing {request.Unit}");
            await BestEffortAsync(() => services.StopAsync(request.Unit, ct));
            await BestEffortAsync(() => services.EnsureEnabledAsync(request.Unit, false, ct));
            await cleaner.DeletePathAsync($"/etc/systemd/system/{request.Unit}", ct);
            await services.ReloadAsync(ct);
        }

        if (!string.IsNullOrWhiteSpace(request.InstallDir))
        {
            sink.Log(LogStream.Note, $"deleting {request.InstallDir}");
            await cleaner.DeletePathAsync(request.InstallDir, ct);
        }

        foreach (string path in request.ConfigPaths)
        {
            await cleaner.DeletePathAsync(path, ct);
        }

        sink.Progress(100, "removed");
        return JobOutcome.Success();
    }

    // Stop/disable of a unit that isn't running or was hand-removed should not fail the whole cleanup.
    private static async Task BestEffortAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch
        {
            // best effort - the deletes below are what "removed" really means.
        }
    }
}
