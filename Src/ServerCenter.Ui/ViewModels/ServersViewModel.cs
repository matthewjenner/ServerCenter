using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerCenter.Ui.Services;

namespace ServerCenter.Ui.ViewModels;

// The Servers read view: what is DEFINED (server instances + their bindings), as opposed to the fleet
// grid which shows live truth. It is a point-in-time read (not a stream) - refreshed on connect, on
// demand (Refresh), and after a definition is stored. Apply() is pure (testable); RefreshAsync() does
// the IO. The client is swapped on reconnect (like the other panels).
public sealed partial class ServersViewModel : ObservableObject
{
    private IAdminClient _client;

    public ServersViewModel(IAdminClient client) => _client = client;

    public void UseClient(IAdminClient client) => _client = client;

    [ObservableProperty] private string _status = string.Empty;

    public ObservableCollection<ServerRowViewModel> Rows { get; } = [];

    public void Apply(IReadOnlyList<ServerInstanceRow> instances)
    {
        Rows.Clear();
        foreach (ServerInstanceRow instance in instances)
        {
            Rows.Add(new ServerRowViewModel(instance));
        }

        Status = $"{Rows.Count} server(s) defined";
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            Apply(await _client.ListServerInstancesAsync(CancellationToken.None));
        }
        catch (Exception ex)
        {
            Status = $"error: {ex.Message}";
        }
    }
}
