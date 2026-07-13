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
    private bool _streamConnected;

    // Raised when the fleet stream (re)connects - i.e. the controller is (back) up. The controller
    // restarting is exactly when its reference data (seeded policies, games) may have changed, so this
    // is the app's cue to refresh the controller-backed dropdowns. Fires once per connect, not per
    // snapshot, so it never floods the controller.
    public event Action? Reconnected;

    [ObservableProperty] private string _connectionStatus = "Connecting...";

    public ObservableCollection<NodeRowViewModel> Nodes { get; } = [];

    // The defined update-policy ids, shared by every card's Update picker (loaded once on connect).
    public ObservableCollection<string> Policies { get; } = [];

    // Live node ids, shared with the Servers tab's add-server node picker.
    public ObservableCollection<string> NodeIds { get; } = [];

    public void UseClients(IJobClient jobs, IAdminClient admin)
    {
        _jobs = jobs;
        _admin = admin;
        _streamConnected = false;   // a fresh connection: let the next snapshot re-announce (re)connect
        _ = RefreshPoliciesAsync();
    }

    // Called by the watch loop when a snapshot arrives (stream up) and when it drops. The up-transition
    // refreshes the shared policy list (card Update pickers) and raises Reconnected once, so a controller
    // that restarted with new seeds is reflected without an app restart - and without polling.
    public void NotifyStreamConnected()
    {
        if (_streamConnected)
        {
            return;
        }

        _streamConnected = true;
        _ = RefreshPoliciesAsync();
        Reconnected?.Invoke();
    }

    public void NotifyStreamDisconnected() => _streamConnected = false;

    // The controller's clock minus ours, captured at each snapshot, so the live "last seen" counter is
    // anchored to controller time but advances on our local clock (keeps counting when the controller
    // is offline, and is skew-proof against a difference between the two machines' clocks).
    private long _controllerOffsetMs;

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // Ticked ~1s by the view so every card's "last seen" keeps advancing without a new snapshot.
    public void RefreshLastSeen()
    {
        long now = NowMs() + _controllerOffsetMs;
        foreach (NodeRowViewModel card in Nodes)
        {
            card.RefreshLastSeen(now);
        }
    }

    public void Apply(FleetSnapshot snapshot)
    {
        _controllerOffsetMs = snapshot.GeneratedUnixMs - NowMs();
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

        // Keep the shared node-id list (add-server picker) in sync with the live fleet.
        foreach (string nodeId in seen)
        {
            if (!NodeIds.Contains(nodeId))
            {
                NodeIds.Add(nodeId);
            }
        }

        for (int i = NodeIds.Count - 1; i >= 0; i--)
        {
            if (!seen.Contains(NodeIds[i]))
            {
                NodeIds.RemoveAt(i);
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
                        NotifyStreamConnected();   // first snapshot after a (re)connect refreshes reference data
                    });
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ConnectionStatus = $"Disconnected: {ex.Message}. Retrying...";
                    NotifyStreamDisconnected();   // arm the next snapshot to re-announce the reconnect
                });
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

    public async Task RefreshPoliciesAsync()
    {
        if (_admin is null)
        {
            return;
        }

        try
        {
            // Called on the UI thread (Connect / the watch loop's snapshot dispatch); the await resumes
            // on the UI sync context. MERGE rather than clear+add so a card's in-flight policy selection
            // survives a refresh (a Clear would null every ComboBox's SelectedItem).
            IReadOnlyList<string> ids = await _admin.ListPolicyIdsAsync(CancellationToken.None);
            foreach (string id in ids)
            {
                if (!Policies.Contains(id))
                {
                    Policies.Add(id);
                }
            }

            for (int i = Policies.Count - 1; i >= 0; i--)
            {
                if (!ids.Contains(Policies[i]))
                {
                    Policies.RemoveAt(i);
                }
            }
        }
        catch
        {
            // best effort - the pickers stay as they are
        }
    }
}
