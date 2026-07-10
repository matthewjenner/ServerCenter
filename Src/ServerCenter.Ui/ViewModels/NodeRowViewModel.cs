using CommunityToolkit.Mvvm.ComponentModel;
using ServerCenter.Contracts.V1;

namespace ServerCenter.Ui.ViewModels;

// One row in the fleet dashboard. Shows dual-truth (brief 3.7): agent-online and VM-running are
// separate columns, with Stale/Unknown as first-class values.
public sealed partial class NodeRowViewModel : ObservableObject
{
    public string NodeId { get; }

    // Property names avoid clashing with the proto enum type names (AgentLiveness, VmState).
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _kind = string.Empty;
    [ObservableProperty] private string _agentLivenessText = string.Empty;
    [ObservableProperty] private string _vmStateText = string.Empty;
    [ObservableProperty] private string _lastSeen = string.Empty;
    [ObservableProperty] private string _resources = string.Empty;
    [ObservableProperty] private string _version = string.Empty;
    [ObservableProperty] private string _os = string.Empty;
    [ObservableProperty] private string _rebootPending = string.Empty;

    public NodeRowViewModel(NodeState node, long generatedUnixMs)
    {
        NodeId = node.NodeId;
        Update(node, generatedUnixMs);
    }

    public void Update(NodeState node, long generatedUnixMs)
    {
        DisplayName = node.DisplayName;
        Kind = node.Kind;
        AgentLivenessText = Format(node.AgentLiveness);
        VmStateText = Format(node.VmState);
        LastSeen = node.LastHeartbeatUnixMs > 0
            ? $"{Math.Max(0, (generatedUnixMs - node.LastHeartbeatUnixMs) / 1000)}s ago"
            : "never";
        Resources = node.Resources is null
            ? "-"
            : $"cpu {node.Resources.CpuPct:0}% / mem {node.Resources.MemUsedPct:0}% / disk {node.Resources.DiskUsedPct:0}%";
        Version = string.IsNullOrEmpty(node.AgentVersion) ? "-" : node.AgentVersion;
        Os = string.IsNullOrEmpty(node.OsFamily)
            ? "-"
            : string.IsNullOrEmpty(node.Arch) ? node.OsFamily : $"{node.OsFamily} {node.Arch}";
        RebootPending = node.RebootPending ? "reboot" : string.Empty;
    }

    private static string Format(AgentLiveness liveness) => liveness switch
    {
        AgentLiveness.Online => "Online",
        AgentLiveness.Stale => "Stale",
        AgentLiveness.Offline => "Offline",
        _ => "Unknown"
    };

    private static string Format(VmState state) => state switch
    {
        VmState.Running => "Running",
        VmState.Stopped => "Stopped",
        _ => "Unknown"
    };
}
