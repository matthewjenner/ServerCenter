using ServerCenter.Core.Jobs;
using ServerCenter.Core.Platform;

namespace ServerCenter.Agent.Linux;

// The "steamcmd" what-provider: keeps the SteamCMD TOOL current (distinct from a game app update,
// which goes through server.install). Update-only-if-present: it LOCATES steamcmd (PATH or a short
// list of common install dirs) and, if found, runs it to self-update IN PLACE - SteamCMD refreshes
// its own bootstrap on every run. If steamcmd isn't on the node it is a no-op success, so the tool is
// never installed where it doesn't belong. Never needs a reboot.
public sealed class SteamCmdUpdateProvider(IProcessRunner runner) : IUpdateProvider
{
    // command -v finds it on PATH; the fallback loop covers common non-PATH installs (systemd's PATH
    // is minimal, and steamcmd is often at /usr/games). Prints the resolved path, or nothing.
    private const string LocateScript =
        "command -v steamcmd 2>/dev/null || " +
        "{ for p in /usr/games/steamcmd /usr/local/bin/steamcmd /usr/bin/steamcmd; do " +
        "[ -x \"$p\" ] && { echo \"$p\"; exit 0; }; done; exit 1; }";

    public string Channel => "steamcmd";

    public async Task<IReadOnlyList<AvailableUpdate>> CheckAsync(CancellationToken ct)
    {
        // SteamCMD has no queryable version to diff; report it as refreshable only when present.
        string? path = await LocateAsync(ct);
        return path is null ? [] : [new AvailableUpdate("steamcmd", path, "latest")];
    }

    public async Task<UpdateOutcome> ApplyAsync(UpdatePlan plan, IJobSink sink, CancellationToken ct)
    {
        string? path = await LocateAsync(ct);
        if (path is null)
        {
            sink.Progress(100, "steamcmd is not installed on this node; skipped");
            return new UpdateOutcome(Success: true, RebootRequired: false, FailReason: null);
        }

        sink.Log(LogStream.Note, $"self-updating steamcmd ({path})");
        ProcessResult result = await runner.RunAsync(path, ["+quit"], ct);
        return result.ExitCode == 0
            ? new UpdateOutcome(Success: true, RebootRequired: false, FailReason: null)
            : new UpdateOutcome(Success: false, RebootRequired: false, $"steamcmd exited {result.ExitCode}: {result.StandardError}");
    }

    public Task<bool> RebootRequiredAsync(CancellationToken ct) => Task.FromResult(false);

    private async Task<string?> LocateAsync(CancellationToken ct)
    {
        ProcessResult result = await runner.RunAsync("sh", ["-c", LocateScript], ct);
        string path = result.StandardOutput.Trim();
        return result.ExitCode == 0 && path.Length > 0 ? path : null;
    }
}
