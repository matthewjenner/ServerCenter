using ServerCenter.Contracts.V1;
using ServerCenter.Controller.Persistence;

namespace ServerCenter.Controller.Services;

// AgentLiveness is intentionally the proto enum here; the Core domain enum is fully qualified
// (ServerCenter.Core.Connection.AgentLiveness) to avoid the name clash.

// Builds the operator fleet view by joining the persisted nodes with live presence and deriving
// dual-truth (brief 3.7): agent-online comes from the heartbeat gap via LivenessTracker;
// VM-running is Unknown until libvirt lands (Phase 6). Testable: nodes + presence + now in,
// snapshot out.
public sealed class FleetSnapshotBuilder(
    AgentNodeRepository nodes,
    AgentPresenceStore presence,
    Core.Connection.LivenessTracker liveness,
    TimeProvider clock)
{
    public async Task<FleetSnapshot> BuildAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        var rows = await nodes.ListNodesAsync(ct);

        var snapshot = new FleetSnapshot { GeneratedUnixMs = now };
        foreach (var row in rows)
        {
            var state = new NodeState
            {
                NodeId = row.NodeId,
                DisplayName = row.DisplayName,
                Kind = row.Kind,
                VmState = VmState.Unknown
            };

            if (presence.TryGet(row.AgentId, out var entry) && entry is not null)
            {
                state.LastHeartbeatUnixMs = entry.LastHeartbeatUnixMs;
                state.AgentLiveness = entry.LastHeartbeatUnixMs > 0
                    ? Map(liveness.Evaluate(entry.LastHeartbeatUnixMs, now))
                    : AgentLiveness.Offline;
                if (entry.LastStatus is not null)
                {
                    state.AgentHealth = entry.LastStatus.AgentHealth;
                    state.Resources = entry.LastStatus.Resources;
                }
            }
            else
            {
                // Node is known but its agent has not contacted this controller.
                state.AgentLiveness = AgentLiveness.Offline;
            }

            snapshot.Nodes.Add(state);
        }

        return snapshot;
    }

    private static AgentLiveness Map(Core.Connection.AgentLiveness liveness) => liveness switch
    {
        Core.Connection.AgentLiveness.Online => AgentLiveness.Online,
        Core.Connection.AgentLiveness.Stale => AgentLiveness.Stale,
        _ => AgentLiveness.Offline
    };
}
