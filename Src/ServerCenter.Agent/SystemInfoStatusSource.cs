using ServerCenter.Contracts.V1;
using ServerCenter.Core.Connection;
using ServerCenter.Core.Platform;

namespace ServerCenter.Agent;

// Builds the NodeStatus the agent pushes each tick from a platform ISystemInfo - real CPU / mem /
// disk / uptime and reboot-pending. Replaces the zero-valued BasicAgentStatusSource on a real node.
public sealed class SystemInfoStatusSource(ISystemInfo systemInfo) : IAgentStatusSource
{
    public async Task<NodeStatus> GetStatusAsync(CancellationToken ct)
    {
        Core.Platform.ResourceSample sample = await systemInfo.SampleAsync(ct);
        SystemFacts facts = await systemInfo.GetFactsAsync(ct);
        bool rebootPending = await systemInfo.RebootPendingAsync(ct);

        return new NodeStatus
        {
            AgentHealth = ServiceHealth.Active,
            RebootPending = rebootPending,
            Resources = new Contracts.V1.ResourceSample
            {
                CpuPct = sample.CpuPct,
                MemUsedPct = sample.MemUsedPct,
                DiskUsedPct = sample.DiskUsedPct,
                UptimeSecs = facts.UptimeSecs
            }
        };
    }
}
