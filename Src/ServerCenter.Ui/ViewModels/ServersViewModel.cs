using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerCenter.Ui.Services;

namespace ServerCenter.Ui.ViewModels;

// The Servers tab: what is DEFINED (server instances + bindings), selection-driven actions on the
// selected server (install / config-apply / recipe-apply, target derived from the row), and a Define
// form to store a descriptor/recipe/instance (paste JSON). A point-in-time read (Refresh), not a
// stream. Apply() is pure (testable); the client is swapped on reconnect.
public sealed partial class ServersViewModel : ObservableObject
{
    private IAdminClient _client;

    public ServersViewModel(IAdminClient client) => _client = client;

    public void UseClient(IAdminClient client) => _client = client;

    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private ServerRowViewModel? _selectedServer;
    [ObservableProperty] private string _actionStatus = string.Empty;

    [ObservableProperty] private string _definitionJson = string.Empty;
    [ObservableProperty] private string _definitionStatus = string.Empty;

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

    // kind (button CommandParameter): "server-install" | "server-config-apply" | "recipe-apply".
    // The target agent + instance come from the selected server row - no hand-typed ids.
    [RelayCommand]
    private async Task ServerJobAsync(string? kind)
    {
        if (SelectedServer is null)
        {
            ActionStatus = "select a server first";
            return;
        }

        if (string.IsNullOrWhiteSpace(kind))
        {
            return;
        }

        try
        {
            string response = await _client.ServerJobAsync(kind, SelectedServer.Node, SelectedServer.Id, CancellationToken.None);
            ActionStatus = $"{kind}: {Short(response)}";
        }
        catch (Exception ex)
        {
            ActionStatus = $"error: {ex.Message}";
        }
    }

    // surface (button CommandParameter): "game-descriptors" | "build-recipes" | "server-instances".
    [RelayCommand]
    private async Task StoreAsync(string? surface)
    {
        if (string.IsNullOrWhiteSpace(surface) || string.IsNullOrWhiteSpace(DefinitionJson))
        {
            DefinitionStatus = "paste a definition first";
            return;
        }

        try
        {
            string response = await _client.StoreAsync(surface, DefinitionJson, CancellationToken.None);
            DefinitionStatus = $"stored {surface}: {Short(response)}";
            await RefreshAsync();   // reflect a newly stored instance in the list
        }
        catch (Exception ex)
        {
            DefinitionStatus = $"error: {ex.Message}";
        }
    }

    private static string Short(string value) => value.Length > 80 ? value[..80] : value;
}
