using ServerCenter.Core.Jobs;

namespace ServerCenter.TestFakes;

// In-memory IAgentJobStateSource. Seed what the agent "remembers" about in-flight jobs so the
// resync report can be driven for each reconciliation case.
public sealed class FakeAgentJobStateSource(params AgentResyncEntry[] entries) : IAgentJobStateSource
{
    private readonly IReadOnlyList<AgentResyncEntry> _entries = entries;

    public Task<IReadOnlyList<AgentResyncEntry>> GetLocalJobStateAsync(CancellationToken ct) =>
        Task.FromResult(_entries);
}
