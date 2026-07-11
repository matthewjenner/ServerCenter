namespace ServerCenter.Core.Platform;

// Host/guest facts and resource sampling. Ships with an in-memory fake.
public interface ISystemInfo
{
    Task<SystemFacts> GetFactsAsync(CancellationToken ct);

    Task<ResourceSample> SampleAsync(CancellationToken ct);

    Task<bool> RebootPendingAsync(CancellationToken ct);

    // The node's systemd service unit names, for the operator's service picker.
    Task<IReadOnlyList<string>> ListServicesAsync(CancellationToken ct);
}

public sealed record SystemFacts(string OsFamily, string OsVersion, string Arch, string Kernel, long UptimeSecs);

public sealed record ResourceSample(double CpuPct, double MemUsedPct, double DiskUsedPct);
