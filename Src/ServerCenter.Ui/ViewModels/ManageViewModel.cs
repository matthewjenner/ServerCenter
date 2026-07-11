using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerCenter.Ui.Services;

namespace ServerCenter.Ui.ViewModels;

// The operator "manage" panel: link a node to its libvirt domain, store a declarative definition
// (paste the JSON), and trigger a server job (install / config-apply / recipe-apply). Writes go to
// the controller's REST endpoints via IAdminClient (swapped on reconnect, like JobsViewModel).
public sealed partial class ManageViewModel : ObservableObject
{
    private IAdminClient _client;

    public ManageViewModel(IAdminClient client) => _client = client;

    public void UseClient(IAdminClient client) => _client = client;

    [ObservableProperty] private string _linkNodeId = string.Empty;
    [ObservableProperty] private string _linkDomain = string.Empty;
    [ObservableProperty] private string _linkStatus = string.Empty;

    [ObservableProperty] private string _definitionJson = string.Empty;
    [ObservableProperty] private string _definitionStatus = string.Empty;

    [ObservableProperty] private string _serverAgentId = string.Empty;
    [ObservableProperty] private string _serverInstanceId = string.Empty;
    [ObservableProperty] private string _serverStatus = string.Empty;

    [RelayCommand]
    private async Task LinkDomainAsync()
    {
        if (string.IsNullOrWhiteSpace(LinkNodeId) || string.IsNullOrWhiteSpace(LinkDomain))
        {
            LinkStatus = "enter a node id and a domain";
            return;
        }

        try
        {
            await _client.LinkDomainAsync(LinkNodeId.Trim(), LinkDomain.Trim(), CancellationToken.None);
            LinkStatus = $"linked {LinkNodeId.Trim()} -> {LinkDomain.Trim()}";
        }
        catch (Exception ex)
        {
            LinkStatus = $"error: {ex.Message}";
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
        }
        catch (Exception ex)
        {
            DefinitionStatus = $"error: {ex.Message}";
        }
    }

    // kind (button CommandParameter): "server-install" | "server-config-apply" | "recipe-apply".
    [RelayCommand]
    private async Task ServerJobAsync(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(ServerAgentId) || string.IsNullOrWhiteSpace(ServerInstanceId))
        {
            ServerStatus = "enter an agent id and an instance id";
            return;
        }

        try
        {
            string response = await _client.ServerJobAsync(kind, ServerAgentId.Trim(), ServerInstanceId.Trim(), CancellationToken.None);
            ServerStatus = $"{kind}: {Short(response)}";
        }
        catch (Exception ex)
        {
            ServerStatus = $"error: {ex.Message}";
        }
    }

    private static string Short(string value) => value.Length > 80 ? value[..80] : value;
}
