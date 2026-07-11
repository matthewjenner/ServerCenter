using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerCenter.Contracts.V1;
using ServerCenter.Ui.Services;

namespace ServerCenter.Ui.ViewModels;

// One node card in the fleet view. Shows dual-truth (agent-online + VM-running as separate values,
// Stale/Unknown first-class) plus real resource bars, and carries this node's own actions - restart a
// service, drive the VM, run an update - so the operator acts on the card, not a shared panel. The
// service list is this node's; the policy list is the shared fleet-wide one, passed in.
public sealed partial class NodeRowViewModel : ObservableObject
{
    private readonly IJobClient? _jobs;
    private readonly IAdminClient? _admin;

    public string NodeId { get; }

    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _kind = string.Empty;
    [ObservableProperty] private string _agentLivenessText = string.Empty;
    [ObservableProperty] private string _vmStateText = string.Empty;
    [ObservableProperty] private string _lastSeen = string.Empty;
    [ObservableProperty] private string _version = string.Empty;
    [ObservableProperty] private string _os = string.Empty;
    [ObservableProperty] private bool _rebootPending;
    [ObservableProperty] private double _cpuPct;
    [ObservableProperty] private double _memPct;
    [ObservableProperty] private double _diskPct;

    [ObservableProperty] private string? _selectedService;
    [ObservableProperty] private string? _selectedPolicy;
    [ObservableProperty] private string _actionStatus = string.Empty;

    public NodeRowViewModel(NodeState node, long generatedUnixMs, IJobClient? jobs, IAdminClient? admin, ObservableCollection<string> policies)
    {
        NodeId = node.NodeId;
        _jobs = jobs;
        _admin = admin;
        Policies = policies;
        Update(node, generatedUnixMs);
        _ = LoadServicesAsync();
    }

    // This node's systemd services (loaded once); the shared fleet-wide policy list.
    public ObservableCollection<string> Services { get; } = [];

    public ObservableCollection<string> Policies { get; }

    public bool IsGuest => Kind == "guest";

    public void Update(NodeState node, long generatedUnixMs)
    {
        DisplayName = node.DisplayName;
        Kind = node.Kind;
        AgentLivenessText = Format(node.AgentLiveness);
        VmStateText = Format(node.VmState);
        LastSeen = node.LastHeartbeatUnixMs > 0
            ? $"{Math.Max(0, (generatedUnixMs - node.LastHeartbeatUnixMs) / 1000)}s ago"
            : "never";
        Version = string.IsNullOrEmpty(node.AgentVersion) ? "-" : node.AgentVersion;
        Os = string.IsNullOrEmpty(node.OsFamily)
            ? "-"
            : string.IsNullOrEmpty(node.Arch) ? node.OsFamily : $"{node.OsFamily} {node.Arch}";
        RebootPending = node.RebootPending;
        CpuPct = node.Resources?.CpuPct ?? 0;
        MemPct = node.Resources?.MemUsedPct ?? 0;
        DiskPct = node.Resources?.DiskUsedPct ?? 0;
    }

    [RelayCommand]
    private async Task RestartServiceAsync()
    {
        if (_jobs is null || string.IsNullOrWhiteSpace(SelectedService))
        {
            ActionStatus = "pick a service";
            return;
        }

        try
        {
            string jobId = await _jobs.RestartServiceAsync(NodeId, SelectedService, CancellationToken.None);
            ActionStatus = $"restart dispatched {Short(jobId)}";
        }
        catch (Exception ex)
        {
            ActionStatus = $"error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task VmActionAsync(string? action)
    {
        if (_jobs is null || string.IsNullOrWhiteSpace(action))
        {
            return;
        }

        try
        {
            UpdateTriggerResult result = await _jobs.TriggerVmActionAsync(NodeId, action, CancellationToken.None);
            ActionStatus = Describe(action, result);
        }
        catch (Exception ex)
        {
            ActionStatus = $"error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RunUpdateAsync()
    {
        if (_jobs is null || string.IsNullOrWhiteSpace(SelectedPolicy))
        {
            ActionStatus = "pick a policy";
            return;
        }

        try
        {
            // The selected service (if any) is the optional service unit for stop-update-start policies.
            UpdateTriggerResult result = await _jobs.TriggerUpdateAsync(
                NodeId, SelectedPolicy, string.IsNullOrWhiteSpace(SelectedService) ? null : SelectedService, CancellationToken.None);
            ActionStatus = Describe("update", result);
        }
        catch (Exception ex)
        {
            ActionStatus = $"error: {ex.Message}";
        }
    }

    private async Task LoadServicesAsync()
    {
        if (_admin is null)
        {
            return;
        }

        try
        {
            foreach (string service in await _admin.ListServicesAsync(NodeId, CancellationToken.None))
            {
                Services.Add(service);
            }
        }
        catch
        {
            // best effort - leave empty
        }
    }

    private static string Describe(string verb, UpdateTriggerResult result) =>
        result is { Outcome: "Dispatched", JobId: { } jobId }
            ? $"{verb} dispatched {Short(jobId)}"
            : $"{result.Outcome}: {result.Reason}";

    private static string Short(string value) => value.Length > 8 ? value[..8] : value;

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
