using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerCenter.Contracts.V1;
using ServerCenter.Ui.Services;

namespace ServerCenter.Ui.ViewModels;

// The Fleet tab: the live node grid (dual-truth) plus selection-driven actions on the SELECTED node -
// restart a service, run an update policy, drive the VM, link a libvirt domain. Actions target the
// selected row (no hand-typed ids). Apply() is pure and UI-thread-agnostic (testable); RunAsync()
// marshals snapshots onto the UI thread and reconnects on failure. The action clients are swapped on
// reconnect via UseClients (the fleet watch client arrives through RunAsync).
public sealed partial class DashboardViewModel : ObservableObject
{
    private IJobClient? _jobs;
    private IAdminClient? _admin;

    [ObservableProperty] private string _connectionStatus = "Connecting...";
    [ObservableProperty] private NodeRowViewModel? _selectedNode;
    [ObservableProperty] private string _actionStatus = string.Empty;

    [ObservableProperty] private string _restartUnit = string.Empty;
    [ObservableProperty] private string _updatePolicyId = string.Empty;
    [ObservableProperty] private string _updateServiceUnit = string.Empty;
    [ObservableProperty] private string _linkDomain = string.Empty;

    public ObservableCollection<NodeRowViewModel> Nodes { get; } = [];

    public void UseClients(IJobClient jobs, IAdminClient admin)
    {
        _jobs = jobs;
        _admin = admin;
    }

    public void Apply(FleetSnapshot snapshot)
    {
        HashSet<string> seen = new HashSet<string>(snapshot.Nodes.Count);
        foreach (NodeState? node in snapshot.Nodes)
        {
            seen.Add(node.NodeId);
            NodeRowViewModel? existing = Nodes.FirstOrDefault(n => n.NodeId == node.NodeId);
            if (existing is null)
            {
                Nodes.Add(new NodeRowViewModel(node, snapshot.GeneratedUnixMs));
            }
            else
            {
                existing.Update(node, snapshot.GeneratedUnixMs);
            }
        }

        for (int i = Nodes.Count - 1; i >= 0; i--)
        {
            if (!seen.Contains(Nodes[i].NodeId))
            {
                Nodes.RemoveAt(i);
            }
        }
    }

    public async Task RunAsync(IFleetClient client, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await foreach (FleetSnapshot snapshot in client.Watch(ct))
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Apply(snapshot);
                        string controller = string.IsNullOrEmpty(snapshot.ControllerVersion)
                            ? string.Empty
                            : $" - controller {snapshot.ControllerVersion}";
                        ConnectionStatus = $"Connected - {snapshot.Nodes.Count} node(s){controller}";
                    });
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() => ConnectionStatus = $"Disconnected: {ex.Message}. Retrying...");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    [RelayCommand]
    private async Task RestartServiceAsync()
    {
        if (!TrySelected(out NodeRowViewModel node) || _jobs is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(RestartUnit))
        {
            ActionStatus = "enter a unit";
            return;
        }

        try
        {
            string jobId = await _jobs.RestartServiceAsync(node.NodeId, RestartUnit.Trim(), CancellationToken.None);
            ActionStatus = $"restart dispatched {Short(jobId)}";
        }
        catch (Exception ex)
        {
            ActionStatus = $"error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RunUpdateAsync()
    {
        if (!TrySelected(out NodeRowViewModel node) || _jobs is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(UpdatePolicyId))
        {
            ActionStatus = "enter a policy id";
            return;
        }

        try
        {
            UpdateTriggerResult result = await _jobs.TriggerUpdateAsync(
                node.NodeId, UpdatePolicyId.Trim(),
                string.IsNullOrWhiteSpace(UpdateServiceUnit) ? null : UpdateServiceUnit.Trim(), CancellationToken.None);
            ActionStatus = Describe("update", result);
        }
        catch (Exception ex)
        {
            ActionStatus = $"error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task VmActionAsync(string? action)
    {
        if (!TrySelected(out NodeRowViewModel node) || _jobs is null || string.IsNullOrWhiteSpace(action))
        {
            return;
        }

        try
        {
            UpdateTriggerResult result = await _jobs.TriggerVmActionAsync(node.NodeId, action, CancellationToken.None);
            ActionStatus = Describe(action, result);
        }
        catch (Exception ex)
        {
            ActionStatus = $"error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LinkDomainAsync()
    {
        if (!TrySelected(out NodeRowViewModel node) || _admin is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(LinkDomain))
        {
            ActionStatus = "enter a domain";
            return;
        }

        try
        {
            await _admin.LinkDomainAsync(node.NodeId, LinkDomain.Trim(), CancellationToken.None);
            ActionStatus = $"linked {node.NodeId} -> {LinkDomain.Trim()}";
        }
        catch (Exception ex)
        {
            ActionStatus = $"error: {ex.Message}";
        }
    }

    private bool TrySelected(out NodeRowViewModel node)
    {
        node = SelectedNode!;
        if (SelectedNode is null)
        {
            ActionStatus = "select a node first";
            return false;
        }

        return true;
    }

    private static string Describe(string verb, UpdateTriggerResult result) =>
        result is { Outcome: "Dispatched", JobId: { } jobId }
            ? $"{verb} dispatched {Short(jobId)}"
            : $"{result.Outcome}: {result.Reason}";

    private static string Short(string value) => value.Length > 8 ? value[..8] : value;
}
