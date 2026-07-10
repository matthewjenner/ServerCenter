using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ServerCenter.Contracts.V1;
using ServerCenter.Ui.Services;

namespace ServerCenter.Ui.ViewModels;

// The live fleet dashboard. Applies snapshots to an observable collection (reconciling add /
// update / remove) and runs a resilient watch loop against the controller. Apply() is pure and
// UI-thread-agnostic so it can be unit-tested; RunAsync() marshals it onto the UI thread.
public sealed partial class DashboardViewModel : ObservableObject
{
    [ObservableProperty] private string _connectionStatus = "Connecting...";

    public ObservableCollection<NodeRowViewModel> Nodes { get; } = [];

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

    // Watches the controller and applies snapshots on the UI thread, reconnecting on failure.
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
                        ConnectionStatus = $"Connected - {snapshot.Nodes.Count} node(s)";
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
}
