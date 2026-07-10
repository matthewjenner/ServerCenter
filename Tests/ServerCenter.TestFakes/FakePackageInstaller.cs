using ServerCenter.Core.Jobs;
using ServerCenter.Core.Platform;

namespace ServerCenter.TestFakes;

// Records requested packages and returns a scripted success, so recipe.apply runs with no apt.
public sealed class FakePackageInstaller(string provider = "apt") : IPackageInstaller
{
    public string Provider { get; } = provider;

    public bool Result { get; set; } = true;

    public List<string> Installed { get; } = [];

    public Task<bool> EnsureInstalledAsync(IReadOnlyList<string> packages, IJobSink sink, CancellationToken ct)
    {
        Installed.AddRange(packages);
        sink.Log(LogStream.Note, $"fake install {string.Join(' ', packages)}");
        return Task.FromResult(Result);
    }
}
