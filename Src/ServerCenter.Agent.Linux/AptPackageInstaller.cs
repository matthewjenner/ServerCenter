using ServerCenter.Core.Jobs;
using ServerCenter.Core.Platform;

namespace ServerCenter.Agent.Linux;

// Ensures apt packages are present (recipe baseRequirements). `apt-get install -y` is convergent -
// already-installed packages are a no-op - so re-applying a recipe is safe. Non-interactive so a
// headless build never stalls on a debconf prompt.
public sealed class AptPackageInstaller(IProcessRunner runner) : IPackageInstaller
{
    private static readonly IReadOnlyDictionary<string, string> NonInteractive =
        new Dictionary<string, string> { ["DEBIAN_FRONTEND"] = "noninteractive" };

    public string Provider => "apt";

    public async Task<bool> EnsureInstalledAsync(IReadOnlyList<string> packages, IJobSink sink, CancellationToken ct)
    {
        if (packages.Count == 0)
        {
            return true;
        }

        sink.Log(LogStream.Note, $"apt-get install {string.Join(' ', packages)}");
        IReadOnlyList<string> args = ["install", "-y", .. packages];
        var result = await runner.RunAsync("apt-get", args, NonInteractive, ct);
        if (result.ExitCode != 0)
        {
            sink.Log(LogStream.Stderr, result.StandardError);
            return false;
        }

        return true;
    }
}
