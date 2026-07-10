namespace ServerCenter.Core.Platform;

// Read-only process inspection. Ships with an in-memory fake.
public interface IProcessInspector
{
    Task<IReadOnlyList<ProcessInfo>> ListAsync(ProcessQuery query, CancellationToken ct);

    Task<ProcessInfo?> FindAsync(ProcessQuery query, CancellationToken ct);
}

public sealed record ProcessQuery(string? NameContains = null, int? Pid = null);

public sealed record ProcessInfo(int Pid, string Name, string? CommandLine, double CpuPct, long WorkingSetBytes);
