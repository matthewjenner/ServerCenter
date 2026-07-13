using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerCenter.Ui.Services;

namespace ServerCenter.Ui.ViewModels;

// Root view-model: owns the controller connection (address + connect/reconnect) and hosts the fleet
// dashboard and jobs panel. Connecting tears down the current streams, re-points the clients at the
// new address, restarts the watch loops, and persists the address for next launch. The client
// factory is injected so this stays testable without real gRPC.
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly Func<string, (IFleetClient Fleet, IJobClient Jobs, IAdminClient Admin)> _clientFactory;
    private readonly ConnectionSettings _settings;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private string _controllerAddress;

    public MainWindowViewModel(
        DashboardViewModel fleet,
        JobsViewModel jobs,
        ServersViewModel servers,
        SettingsViewModel settingsTab,
        Func<string, (IFleetClient Fleet, IJobClient Jobs, IAdminClient Admin)> clientFactory,
        ConnectionSettings settings,
        UpdateBannerViewModel? update = null)
    {
        Fleet = fleet;
        Jobs = jobs;
        Servers = servers;
        Settings = settingsTab;
        Update = update ?? new UpdateBannerViewModel();
        _clientFactory = clientFactory;
        _settings = settings;
        _controllerAddress = settings.ResolveStartupAddress();

        // When the fleet stream (re)connects, the controller is (back) up - refresh the OTHER
        // controller-backed dropdowns too (the Fleet tab refreshes its own policy list). Keeps seeded/
        // new policies, games, and instances current after a controller restart without an app reload.
        Fleet.Reconnected += OnControllerReconnected;
    }

    private void OnControllerReconnected()
    {
        Settings.RefreshPoliciesCommand.Execute(null);   // Settings-tab policy list
        Servers.RefreshCommand.Execute(null);            // defined instances + the game picker
    }

    public DashboardViewModel Fleet { get; }

    public JobsViewModel Jobs { get; }

    public ServersViewModel Servers { get; }

    public SettingsViewModel Settings { get; }

    // Self-update banner (Velopack). Disabled no-op instance when no update service is supplied.
    public UpdateBannerViewModel Update { get; }

    // Connect or reconnect to ControllerAddress: cancel the current streams, point both clients at
    // the new address, clear the now-stale rows, restart the watch loops, and persist the address.
    [RelayCommand]
    private void Connect()
    {
        string address = (ControllerAddress ?? string.Empty).Trim();
        if (address.Length == 0)
        {
            Fleet.ConnectionStatus = "Enter a controller address";
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        CancellationToken ct = _cts.Token;

        (IFleetClient fleetClient, IJobClient jobClient, IAdminClient adminClient) = _clientFactory(address);
        Jobs.UseClient(jobClient);
        Fleet.UseClients(jobClient, adminClient);
        Servers.UseClient(adminClient);
        Settings.UseClient(adminClient);
        Servers.Rows.Clear();
        Fleet.Nodes.Clear();
        Jobs.Jobs.Clear();
        Fleet.ConnectionStatus = $"Connecting to {address}...";
        _settings.Save(address);

        _ = Fleet.RunAsync(fleetClient, ct);
        _ = Jobs.RunAsync(ct);
        Servers.RefreshCommand.Execute(null);   // point-in-time read of what is defined
    }

    public void Dispose()
    {
        Fleet.Reconnected -= OnControllerReconnected;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
