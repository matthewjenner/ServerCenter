using ServerCenter.Contracts.V1;
using ServerCenter.Core.Connection;

namespace ServerCenter.TestFakes;

// Returns a canned NodeStatus for the agent to push. Defaults to a healthy node.
public sealed class FakeAgentStatusSource(NodeStatus? status = null) : IAgentStatusSource
{
    private readonly NodeStatus _status = status ?? new NodeStatus
    {
        AgentHealth = ServiceHealth.Active,
        RebootPending = false,
        Resources = new ResourceSample { CpuPct = 1, MemUsedPct = 2, DiskUsedPct = 3, UptimeSecs = 100 }
    };

    public Task<NodeStatus> GetStatusAsync(CancellationToken ct) => Task.FromResult(_status);
}
