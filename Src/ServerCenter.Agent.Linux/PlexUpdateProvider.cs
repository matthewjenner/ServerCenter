using System.Text.Json;
using System.Text.Json.Serialization;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Platform;

namespace ServerCenter.Agent.Linux;

// The Plex "what" provider: the deliberately non-apt backend that proves IUpdateProvider is a real
// abstraction and not "apt with extra steps" (phase-0-contracts.md 7). Plex ships its own app
// channel - a downloads manifest plus a versioned .deb - so this provider queries the manifest over
// HTTP, compares against the installed dpkg version, and applies by downloading the matching .deb
// and running dpkg -i. An app update never needs a host reboot, so RebootRequired is always false.
public sealed class PlexUpdateProvider(
    IHttpFetcher http, IProcessRunner runner, PlexUpdateOptions options) : IUpdateProvider
{
    private const string PackageName = "plexmediaserver";

    private static readonly IReadOnlyDictionary<string, string> NonInteractive =
        new Dictionary<string, string> { ["DEBIAN_FRONTEND"] = "noninteractive" };

    public string Channel => "plex";

    public async Task<IReadOnlyList<AvailableUpdate>> CheckAsync(CancellationToken ct)
    {
        PlexRelease release = await FetchReleaseAsync(ct);
        string installed = await InstalledVersionAsync(ct);
        return string.Equals(installed, release.Version, StringComparison.Ordinal)
            ? []
            : [new AvailableUpdate(PackageName, installed.Length == 0 ? "(none)" : installed, release.Version)];
    }

    public async Task<UpdateOutcome> ApplyAsync(UpdatePlan plan, IJobSink sink, CancellationToken ct)
    {
        // Update-only-if-present: never INSTALL Plex on a node that doesn't have it. A "plex" policy
        // dispatched fleet-wide only touches the Plex box; everywhere else this is a no-op success.
        string installed = await InstalledVersionAsync(ct);
        if (installed.Length == 0)
        {
            sink.Progress(100, "Plex is not installed on this node; skipped");
            return new UpdateOutcome(Success: true, RebootRequired: false, FailReason: null);
        }

        PlexRelease release = await FetchReleaseAsync(ct);

        string destination = Path.Combine(options.DownloadDirectory, $"plexmediaserver-{release.Version}.deb");
        sink.Log(LogStream.Note, $"downloading Plex {release.Version}");
        await http.DownloadToFileAsync(release.Url, destination, ct);

        sink.Log(LogStream.Note, $"dpkg -i {Path.GetFileName(destination)}");
        ProcessResult install = await runner.RunAsync("dpkg", ["-i", destination], NonInteractive, ct);
        if (install.ExitCode != 0)
        {
            return new UpdateOutcome(Success: false, RebootRequired: false, $"dpkg -i failed: {install.StandardError}");
        }

        sink.Progress(100, $"installed Plex {release.Version}");
        return new UpdateOutcome(Success: true, RebootRequired: false, FailReason: null);
    }

    // An app-channel update replaces the service binary in place; it never requires a host reboot.
    public Task<bool> RebootRequiredAsync(CancellationToken ct) => Task.FromResult(false);

    private async Task<PlexRelease> FetchReleaseAsync(CancellationToken ct)
    {
        string json = await http.GetStringAsync(options.DownloadsUrl, ct);
        PlexDownloads manifest = JsonSerializer.Deserialize<PlexDownloads>(json)
            ?? throw new InvalidOperationException("Plex downloads manifest was empty");

        PlexPlatform platform = manifest.Computer.Linux
            ?? throw new InvalidOperationException("Plex downloads manifest has no Linux platform");

        // Match the Debian/Ubuntu build for this node's arch (e.g. linux-x86_64, linux-aarch64).
        PlexManifestRelease? release = platform.Releases.FirstOrDefault(r =>
            string.Equals(r.Distro, "debian", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.Build, options.Build, StringComparison.OrdinalIgnoreCase));
        if (release is null)
        {
            throw new InvalidOperationException(
                $"no Plex debian release for build '{options.Build}' in the manifest");
        }

        return new PlexRelease(platform.Version, release.Url);
    }

    private async Task<string> InstalledVersionAsync(CancellationToken ct)
    {
        ProcessResult result = await runner.RunAsync("dpkg-query", ["-W", "-f=${Version}", PackageName], ct);
        // dpkg-query exits non-zero when the package is not installed; treat that as "none".
        return result.ExitCode == 0 ? result.StandardOutput.Trim() : string.Empty;
    }

    private sealed record PlexRelease(string Version, string Url);

    private sealed record PlexDownloads(
        [property: JsonPropertyName("computer")] PlexComputer Computer);

    private sealed record PlexComputer(
        [property: JsonPropertyName("Linux")] PlexPlatform? Linux);

    private sealed record PlexPlatform(
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("releases")] IReadOnlyList<PlexManifestRelease> Releases);

    private sealed record PlexManifestRelease(
        [property: JsonPropertyName("build")] string Build,
        [property: JsonPropertyName("distro")] string Distro,
        [property: JsonPropertyName("url")] string Url);
}

// Instance params for the Plex channel: where the manifest lives, which arch build to pick, and
// where to stage the downloaded .deb. Defaults target the public (anonymous) channel on x86-64.
public sealed record PlexUpdateOptions
{
    public string DownloadsUrl { get; init; } = "https://plex.tv/api/downloads/5.json";

    public string Build { get; init; } = "linux-x86_64";

    public string DownloadDirectory { get; init; } = "/tmp";
}
