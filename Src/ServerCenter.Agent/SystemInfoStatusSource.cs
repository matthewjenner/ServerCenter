using ServerCenter.Contracts.V1;
using ServerCenter.Core.Connection;
using ServerCenter.Core.Platform;

namespace ServerCenter.Agent;

// Builds the NodeStatus the agent pushes each tick from a platform ISystemInfo - real CPU / mem /
// disk / uptime, reboot-pending, and the systemd service list (for the operator's service picker).
// The service list changes slowly and enumerating it spawns a subprocess, so it is refreshed only
// every Nth tick and reused in between; CPU/mem/reboot are always fresh.
public sealed class SystemInfoStatusSource(ISystemInfo systemInfo) : IAgentStatusSource
{
    private const int ServiceRefreshEveryTicks = 6;   // ~ once a minute at a 10s heartbeat

    private IReadOnlyList<string> _services = [];
    private int _tick;

    public async Task<NodeStatus> GetStatusAsync(CancellationToken ct)
    {
        Core.Platform.ResourceSample sample = await systemInfo.SampleAsync(ct);
        SystemFacts facts = await systemInfo.GetFactsAsync(ct);
        bool rebootPending = await systemInfo.RebootPendingAsync(ct);

        if (_tick++ % ServiceRefreshEveryTicks == 0)
        {
            try
            {
                _services = await systemInfo.ListServicesAsync(ct);
            }
            catch
            {
                // keep the last known list on a transient failure
            }
        }

        NodeStatus status = new NodeStatus
        {
            AgentHealth = ServiceHealth.Active,
            RebootPending = rebootPending,
            Resources = new Contracts.V1.ResourceSample
            {
                CpuPct = sample.CpuPct,
                MemUsedPct = sample.MemUsedPct,
                DiskUsedPct = sample.DiskUsedPct,
                UptimeSecs = facts.UptimeSecs,
                MemTotalBytes = sample.MemTotalBytes,
                MemUsedBytes = sample.MemUsedBytes,
                DiskTotalBytes = sample.DiskTotalBytes,
                DiskUsedBytes = sample.DiskUsedBytes,
                CpuCores = sample.CpuCores
            }
        };
        status.Services.AddRange(_services);
        return status;
    }
}
