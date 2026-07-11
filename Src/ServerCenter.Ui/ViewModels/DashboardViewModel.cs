using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ServerCenter.Contracts.V1;
using ServerCenter.Ui.Services;

namespace ServerCenter.Ui.ViewModels;

// The Fleet tab: a live card per node (dual-truth + resource bars). Actions live ON each card
// (NodeRowViewModel), so this view-model just owns the node list, the fleet watch, and the shared
// fleet-wide policy list handed to every card. Apply() is pure/testable; RunAsync() marshals snapshots
// onto the UI thread and reconnects on failure. Action clients arrive via UseClients (the fleet watch
// client comes through RunAsync) and are handed to each card as it is created.
public sealed partial class DashboardViewModel : ObservableObject
{
    private IJobClient? _jobs;
    private IAdminClient? _admin;

    [ObservableProperty] private string _connectionStatus = "Connecting...";

    public ObservableCollection<NodeRowViewModel> Nodes { get; } = [];

    // The defined update-policy ids, shared by every card's Update picker (loaded once on connect).
    public ObservableCollection<string> Policies { get; } = [];

    public void UseClients(IJobClient jobs, IAdminClient admin)
    {
        _jobs = jobs;
        _admin = admin;
        _ = LoadPoliciesAsync();
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
                Nodes.Add(new NodeRowViewModel(node, snapshot.GeneratedUnixMs, _jobs, _admin, Policies));
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

    private async Task LoadPoliciesAsync()
    {
        if (_admin is null)
        {
            return;
        }

        try
        {
            // Called from Connect on the UI thread; the await resumes on the UI sync context.
            IReadOnlyList<string> ids = await _admin.ListPolicyIdsAsync(CancellationToken.None);
            Policies.Clear();
            foreach (string id in ids)
            {
                Policies.Add(id);
            }
        }
        catch
        {
            // best effort - the pickers stay empty
        }
    }
}
