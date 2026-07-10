using ServerCenter.Contracts.V1;
using ServerCenter.Controller.Persistence;
using ServerCenter.Core.Primitives;

namespace ServerCenter.Controller.Services;

// AgentLiveness is intentionally the proto enum here; the Core domain enum is fully qualified
// (ServerCenter.Core.Connection.AgentLiveness) to avoid the name clash.

// Builds the operator fleet view by joining the persisted nodes with live presence and deriving
// dual-truth (brief 3.7): agent-online comes from the heartbeat gap via LivenessTracker; VM-running
// comes from the node's libvirt domain state (LibvirtDomainStates). The two are INDEPENDENT facts
// that can disagree; a node with no linked domain, or a domain libvirt hasn't reported, stays
// Unknown. Testable: nodes + presence + domain states + now in, snapshot out.
public sealed class FleetSnapshotBuilder(
    AgentNodeRepository nodes,
    AgentPresenceStore presence,
    LibvirtDomainStates domainStates,
    Core.Connection.LivenessTracker liveness,
    TimeProvider clock)
{
    public async Task<FleetSnapshot> BuildAsync(CancellationToken ct)
    {
        long now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        IReadOnlyList<NodeRow> rows = await nodes.ListNodesAsync(ct);

        FleetSnapshot snapshot = new FleetSnapshot { GeneratedUnixMs = now };
        foreach (NodeRow row in rows)
        {
            NodeState state = new NodeState
            {
                NodeId = row.NodeId,
                DisplayName = row.DisplayName,
                Kind = row.Kind,
                VmState = DeriveVmState(row.LibvirtDomain)
            };

            if (presence.TryGet(row.AgentId, out AgentPresence? entry) && entry is not null)
            {
                state.LastHeartbeatUnixMs = entry.LastHeartbeatUnixMs;
                state.AgentLiveness = entry.LastHeartbeatUnixMs > 0
                    ? Map(liveness.Evaluate(entry.LastHeartbeatUnixMs, now))
                    : AgentLiveness.Offline;
                state.AgentVersion = entry.AgentVersion;
                state.OsFamily = entry.OsFamily;
                state.Arch = entry.Arch;
                if (entry.LastStatus is not null)
                {
                    state.AgentHealth = entry.LastStatus.AgentHealth;
                    state.Resources = entry.LastStatus.Resources;
                    state.RebootPending = entry.LastStatus.RebootPending;
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

    // A node with no linked domain, or one libvirt has not reported, is Unknown - not "Stopped".
    // Unknown is a first-class dual-truth state, never a lying green/red dot (brief 3.7).
    private VmState DeriveVmState(string? libvirtDomain) =>
        !string.IsNullOrEmpty(libvirtDomain) && domainStates.TryGet(libvirtDomain, out DomainState domainState)
            ? MapVm(domainState)
            : VmState.Unknown;

    private static VmState MapVm(DomainState state) => state switch
    {
        DomainState.Running => VmState.Running,
        DomainState.ShutOff or DomainState.Shutdown or DomainState.Crashed or DomainState.Paused => VmState.Stopped,
        _ => VmState.Unknown
    };

    private static AgentLiveness Map(Core.Connection.AgentLiveness liveness) => liveness switch
    {
        Core.Connection.AgentLiveness.Online => AgentLiveness.Online,
        Core.Connection.AgentLiveness.Stale => AgentLiveness.Stale,
        _ => AgentLiveness.Offline
    };
}
