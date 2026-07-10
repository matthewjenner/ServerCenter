using ServerCenter.Core.Jobs;

namespace ServerCenter.Core.Primitives;

// The SteamCMD primitive (brief 7): anonymous install/update of a dedicated-server app. Every server
// we run is an anonymous SteamCMD app, so one convergent operation (ensure-installed = install =
// repair = update) covers install and update. Behind a seam because it reaches real infra (multi-GB
// downloads) - it ships a fake so Tier 1 drives the capability layer with zero Steam.
public interface ISteamCmd
{
    // Anonymous +app_update <appid> validate to the install dir. Convergent: running it on an
    // up-to-date install re-validates (a near no-op), so build = repair = update.
    Task<SteamAppResult> EnsureAppAsync(SteamAppRequest request, IJobSink sink, CancellationToken ct);

    // The installed build id from the app manifest, or null if the app is not installed. Comparing it
    // before/after an EnsureApp is the buildid-based update-detect.
    Task<string?> GetInstalledBuildIdAsync(string installDir, long appId, CancellationToken ct);
}

public sealed record SteamAppRequest(long AppId, string InstallDir, string? BetaBranch = null, bool Validate = true);

public sealed record SteamAppResult(bool Success, string? BuildId, string? FailReason);
