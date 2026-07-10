using ServerCenter.Core.Jobs;
using ServerCenter.Core.Platform;

namespace ServerCenter.Agent.Linux;

// The apt "what" provider (phase-0-contracts.md 7): one of the two backends that prove the
// IUpdateProvider abstraction is not apt-only. Command construction + output parsing are unit
// tested against a fake IProcessRunner; the real apt run smokes on a Linux node (Tier 2). Every
// invocation is non-interactive (DEBIAN_FRONTEND) so a headless job never stalls on a debconf
// prompt.
public sealed class AptUpdateProvider(IProcessRunner runner) : IUpdateProvider
{
    private const string RebootRequiredFlag = "/var/run/reboot-required";

    private static readonly IReadOnlyDictionary<string, string> NonInteractive =
        new Dictionary<string, string> { ["DEBIAN_FRONTEND"] = "noninteractive" };

    public string Channel => "apt";

    public async Task<IReadOnlyList<AvailableUpdate>> CheckAsync(CancellationToken ct)
    {
        await RunOrThrowAsync("apt-get", ["update"], ct);
        var listing = await runner.RunAsync("apt", ["list", "--upgradable"], NonInteractive, ct);
        return ParseUpgradable(listing.StandardOutput);
    }

    public async Task<UpdateOutcome> ApplyAsync(UpdatePlan plan, IJobSink sink, CancellationToken ct)
    {
        sink.Log(LogStream.Note, "apt-get update");
        var refresh = await runner.RunAsync("apt-get", ["update"], NonInteractive, ct);
        if (refresh.ExitCode != 0)
        {
            return new UpdateOutcome(Success: false, RebootRequired: false, $"apt-get update failed: {refresh.StandardError}");
        }

        // Targeted upgrade when packages are named; a full upgrade when the plan is open-ended.
        IReadOnlyList<string> arguments = plan.Packages.Count > 0
            ? ["install", "-y", "--only-upgrade", .. plan.Packages]
            : ["upgrade", "-y"];

        sink.Log(LogStream.Note, $"apt-get {string.Join(' ', arguments)}");
        var apply = await runner.RunAsync("apt-get", arguments, NonInteractive, ct);
        if (apply.ExitCode != 0)
        {
            return new UpdateOutcome(Success: false, RebootRequired: false, $"apt-get failed: {apply.StandardError}");
        }

        var rebootRequired = await RebootRequiredAsync(ct);
        sink.Progress(100, rebootRequired ? "updated; reboot required" : "updated");
        return new UpdateOutcome(Success: true, rebootRequired, FailReason: null);
    }

    // apt sets the reboot-required flag file when a package (e.g. the kernel) needs a restart. `test`
    // returns 1 (not an error) when the file is absent, so exit code IS the answer - never throw here.
    public async Task<bool> RebootRequiredAsync(CancellationToken ct)
    {
        var result = await runner.RunAsync("test", ["-f", RebootRequiredFlag], ct);
        return result.ExitCode == 0;
    }

    private async Task RunOrThrowAsync(string file, IReadOnlyList<string> args, CancellationToken ct)
    {
        var result = await runner.RunAsync(file, args, NonInteractive, ct);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{file} {string.Join(' ', args)} failed (exit {result.ExitCode}): {result.StandardError}");
        }
    }

    // Parses `apt list --upgradable` lines, e.g.
    //   zlib1g/jammy-updates 1:1.2.11.dfsg-2ubuntu9.2 amd64 [upgradable from: 1:1.2.11.dfsg-2ubuntu9]
    // into (package, currentVersion, targetVersion). Header/other lines lack the marker and are skipped.
    private static IReadOnlyList<AvailableUpdate> ParseUpgradable(string stdout)
    {
        const string marker = "[upgradable from:";
        var updates = new List<AvailableUpdate>();
        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.Trim();
            var at = line.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0)
            {
                continue;
            }

            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields.Length < 2)
            {
                continue;
            }

            var slash = fields[0].IndexOf('/');
            var package = slash < 0 ? fields[0] : fields[0][..slash];
            var target = fields[1];
            var current = line[(at + marker.Length)..].Trim().TrimEnd(']').Trim();
            updates.Add(new AvailableUpdate(package, current, target));
        }

        return updates;
    }
}
