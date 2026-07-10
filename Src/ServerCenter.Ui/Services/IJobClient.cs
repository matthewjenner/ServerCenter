using ServerCenter.Contracts.V1;

namespace ServerCenter.Ui.Services;

// The dashboard's jobs source + trigger. Behind an interface so the jobs view-model can be tested
// with a fake (no controller / gRPC).
public interface IJobClient
{
    IAsyncEnumerable<JobListSnapshot> Watch(CancellationToken ct);

    Task<string> RestartServiceAsync(string agentId, string unit, CancellationToken ct);
}
