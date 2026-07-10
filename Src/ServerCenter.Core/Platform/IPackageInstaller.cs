using ServerCenter.Core.Jobs;

namespace ServerCenter.Core.Platform;

// Ensures a set of packages is present (a recipe's baseRequirements). Distinct from IUpdateProvider,
// which UPDATES what is installed; this INSTALLS what a build needs. Convergent: already-installed
// packages are a no-op (apt-get install is idempotent). Ships a fake.
public interface IPackageInstaller
{
    string Provider { get; } // "apt" | ...

    Task<bool> EnsureInstalledAsync(IReadOnlyList<string> packages, IJobSink sink, CancellationToken ct);
}
