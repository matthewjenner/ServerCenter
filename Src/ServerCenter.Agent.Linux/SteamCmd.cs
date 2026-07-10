using System.Globalization;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Primitives;

namespace ServerCenter.Agent.Linux;

// The SteamCMD primitive over the process runner (same testable pattern as the apt/Plex providers).
// Command construction + success parsing are unit tested against a fake runner; the real multi-GB
// install smokes on a Linux node (Tier 2). Anonymous login only - every server we run is an
// anonymous dedicated-server app.
public sealed class SteamCmd(IProcessRunner runner, string executable = "steamcmd") : ISteamCmd
{
    // steamcmd prints this on a successful install/update.
    private const string SuccessMarker = "fully installed";

    public async Task<SteamAppResult> EnsureAppAsync(SteamAppRequest request, IJobSink sink, CancellationToken ct)
    {
        var appId = request.AppId.ToString(CultureInfo.InvariantCulture);

        // force_install_dir must precede app_update; login must precede it too.
        var args = new List<string>
        {
            "+force_install_dir", request.InstallDir,
            "+login", "anonymous",
            "+app_update", appId
        };
        if (!string.IsNullOrWhiteSpace(request.BetaBranch))
        {
            args.Add("-beta");
            args.Add(request.BetaBranch);
        }

        if (request.Validate)
        {
            args.Add("validate");
        }

        args.Add("+quit");

        sink.Log(LogStream.Note, $"steamcmd +app_update {appId}{(request.Validate ? " validate" : string.Empty)}");
        var result = await runner.RunAsync(executable, args, ct);

        // buildid is read best-effort from the manifest (absent in a dry unit test -> null).
        var buildId = await GetInstalledBuildIdAsync(request.InstallDir, request.AppId, ct);

        if (result.ExitCode != 0)
        {
            return new SteamAppResult(Success: false, buildId, $"steamcmd exited {result.ExitCode}: {result.StandardError}");
        }

        return result.StandardOutput.Contains(SuccessMarker, StringComparison.OrdinalIgnoreCase)
            ? new SteamAppResult(Success: true, buildId, FailReason: null)
            : new SteamAppResult(Success: false, buildId, "steamcmd did not report a successful install");
    }

    public async Task<string?> GetInstalledBuildIdAsync(string installDir, long appId, CancellationToken ct)
    {
        var manifest = Path.Combine(
            installDir, "steamapps", $"appmanifest_{appId.ToString(CultureInfo.InvariantCulture)}.acf");
        if (!File.Exists(manifest))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(manifest, ct);
        return SteamAppManifest.ParseBuildId(content);
    }
}
