using System.Text.Json;
using ServerCenter.Core.Games;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Primitives;

namespace ServerCenter.Agent.Jobs;

// Executes a "server.install" job: anonymous SteamCMD install/update of the descriptor's app to its
// install dir. Convergent (SteamCMD ensure = install = repair = update), so the job is requeueable.
public sealed class ServerInstallExecutor(ISteamCmd steam) : IJobExecutor
{
    public string JobType => "server.install";

    public async Task<JobOutcome> ExecuteAsync(JobContext context, IJobSink sink, CancellationToken ct)
    {
        ServerInstallParams request;
        try
        {
            request = ServerJobParamsSerializer.Deserialize<ServerInstallParams>(context.ParamsJson);
        }
        catch (JsonException ex)
        {
            return JobOutcome.Failure($"invalid server.install params: {ex.Message}");
        }

        var result = await steam.EnsureAppAsync(
            new SteamAppRequest(request.AppId, request.InstallDir, request.BetaBranch, request.Validate), sink, ct);

        return result.Success
            ? JobOutcome.Success()
            : JobOutcome.Failure(result.FailReason ?? "steamcmd install failed");
    }
}
