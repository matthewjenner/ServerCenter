using Avalonia.Threading;
using Velopack;
using Velopack.Sources;

namespace ServerCenter.Ui.Services;

// Polls the ServerCenter GitHub releases for a newer UI build and raises an event a banner binds to.
// First check fires shortly after startup, then hourly. Any failure (offline, no releases yet, dev
// mode) is swallowed - the banner just stays hidden and the next tick retries.
//
// Dev runs (dotnet run) are not "installed" from Velopack's perspective, so UpdateManager.IsInstalled
// is false and InstallAndRestartAsync is a no-op; the banner can still show for UI testing but Install
// stays disabled. The installed app (from the ui-v* Velopack release) is the one that self-updates.
public sealed class UpdateService : IDisposable
{
    private const string GitHubRepoUrl = "https://github.com/matthewjenner/ServerCenter";
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);

    // DEV cadence: 5 minutes, so a freshly published ui-v* release is picked up quickly while
    // iterating (mirrors the agent/controller auto-update dev cadence). EARMARKED to become a
    // user/controller-managed setting (with a saner default like hourly/daily) rather than a constant.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    private readonly UpdateManager? _manager;
    private readonly CancellationTokenSource _cts = new();
    private UpdateInfo? _pending;
    private string? _skipped;

    public UpdateService()
    {
        try
        {
            // prerelease:false - only the stable ui-v* releases. The GithubSource skips releases with
            // no Velopack assets (the agent-v / controller-v ones), so mixed tags are fine.
            _manager = new UpdateManager(new GithubSource(GitHubRepoUrl, accessToken: null, prerelease: false));
        }
        catch
        {
            // Malformed URL / offline at construction - leave it null; the app runs without updates.
            _manager = null;
        }

        _ = Task.Run(() => PollLoopAsync(_cts.Token));
    }

    // Raised on the UI thread when an update appears, disappears, or is dismissed.
    public event Action<string?>? UpdateAvailableChanged;

    // Semver of the available update, or null when none is offered.
    public string? AvailableVersion { get; private set; }

    // True only when Velopack is in installed mode (i.e. not a dotnet run).
    public bool CanInstall => _manager?.IsInstalled ?? false;

    // Downloads the pending update and restarts into the new version.
    public async Task InstallAndRestartAsync()
    {
        if (_manager is null || _pending is null || !_manager.IsInstalled)
        {
            return;
        }

        try
        {
            await _manager.DownloadUpdatesAsync(_pending);
            _manager.ApplyUpdatesAndRestart(_pending);
        }
        catch
        {
            // Best-effort: a failed apply leaves the banner up so the user can try again.
        }
    }

    // Hides the banner until a newer version than this one appears.
    public void SkipCurrentVersion()
    {
        _skipped = AvailableVersion;
        SetAvailable(null);
    }

    // Force an immediate check (the Help > Check for Updates menu item), returning the available
    // version or null. Independent of the background poll; safe to call any time.
    public Task<string?> CheckNowAsync() => CheckOnceAsync();

    private async Task PollLoopAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(StartupDelay, ct);
            while (!ct.IsCancellationRequested)
            {
                await CheckOnceAsync();
                await Task.Delay(PollInterval, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal on shutdown.
        }
    }

    private async Task<string?> CheckOnceAsync()
    {
        if (_manager is null)
        {
            return null;
        }

        try
        {
            UpdateInfo? info = await _manager.CheckForUpdatesAsync();
            if (info is null)
            {
                SetAvailable(null);
                return null;
            }

            string version = info.TargetFullRelease.Version.ToString();
            if (string.Equals(version, _skipped, StringComparison.Ordinal))
            {
                SetAvailable(null);
                return null;
            }

            _pending = info;
            SetAvailable(version);
            return version;
        }
        catch
        {
            // Network down, no releases yet, dev mode without an installed app, etc. Stay quiet.
            return null;
        }
    }

    private void SetAvailable(string? version)
    {
        if (string.Equals(version, AvailableVersion, StringComparison.Ordinal))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            AvailableVersion = version;
            UpdateAvailableChanged?.Invoke(version);
        });
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
