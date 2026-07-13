using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerCenter.Ui.Services;

namespace ServerCenter.Ui.ViewModels;

// The Servers tab: the game-server section. Lists defined instances, and drives the guided flow to
// ADD one (pick a seeded game + a node + a name + params -> a server_instance) and REMOVE one (cleanup
// job + row delete). Actions (install / config-apply / recipe-apply) target the selected instance.
// The node list is shared live from the fleet; games come from the controller's descriptor/recipe store.
public sealed partial class ServersViewModel : ObservableObject
{
    // Starter instance params per game id, prefilled when a game is picked so the operator edits rather
    // than authors from scratch. CS2: hostname/map/ports/gslt/rcon; ports.game +2 per extra instance.
    private const string Cs2ParamsTemplate =
        "{\n" +
        "  \"name\": \"arena1\",\n" +
        "  \"hostname\": \"ServerCenter CS2\",\n" +
        "  \"map\": \"de_dust2\",\n" +
        "  \"game\": { \"type\": \"0\", \"mode\": \"1\" },\n" +
        "  \"ports\": { \"game\": 27015 },\n" +
        "  \"gslt\": \"\",\n" +
        "  \"rcon\": { \"password\": \"changeme\" },\n" +
        "  \"sv\": { \"lan\": \"0\" }\n" +
        "}";

    private IAdminClient _client;

    public ServersViewModel(IAdminClient client, ObservableCollection<string>? nodes = null)
    {
        _client = client;
        Nodes = nodes ?? [];
    }

    public void UseClient(IAdminClient client) => _client = client;

    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private ServerRowViewModel? _selectedServer;
    [ObservableProperty] private string _actionStatus = string.Empty;

    // Add-server form.
    [ObservableProperty] private GameOption? _selectedGame;
    [ObservableProperty] private string? _selectedNode;
    [ObservableProperty] private string _newName = string.Empty;
    [ObservableProperty] private string _newParamsJson = string.Empty;
    [ObservableProperty] private string _createStatus = string.Empty;

    // Legacy raw-define (paste a descriptor/recipe/instance JSON), kept for power users.
    [ObservableProperty] private string _definitionJson = string.Empty;
    [ObservableProperty] private string _definitionStatus = string.Empty;

    public ObservableCollection<ServerRowViewModel> Rows { get; } = [];

    public ObservableCollection<GameOption> Games { get; } = [];

    // The nodes a server can be placed on (shared live from the fleet dashboard).
    public ObservableCollection<string> Nodes { get; }

    // Prefill the params editor with the picked game's starter template (only when the box is empty or
    // still holds a prior template, so an in-progress edit is never clobbered).
    partial void OnSelectedGameChanged(GameOption? value)
    {
        if (value is not null && (string.IsNullOrWhiteSpace(NewParamsJson) || NewParamsJson == Cs2ParamsTemplate))
        {
            NewParamsJson = value.Id == "cs2" ? Cs2ParamsTemplate : "{\n}";
        }
    }

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

        try
        {
            IReadOnlyList<GameOption> games = await _client.ListGamesAsync(CancellationToken.None);
            Games.Clear();
            foreach (GameOption game in games)
            {
                Games.Add(game);
            }
        }
        catch
        {
            // best effort - the picker just stays empty
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (SelectedGame is null)
        {
            CreateStatus = "pick a game";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedNode))
        {
            CreateStatus = "pick a node";
            return;
        }

        string id = Slug(NewName);
        if (id.Length == 0)
        {
            CreateStatus = "enter a name";
            return;
        }

        // The instance id is the uniqueness/scoping key ({{instance.id}}); reject a collision on the node.
        if (Rows.Any(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase) && r.Node == SelectedNode))
        {
            CreateStatus = $"'{id}' already exists on {SelectedNode}";
            return;
        }

        string body = JsonSerializer.Serialize(new
        {
            Id = id,
            NodeId = SelectedNode,
            DescriptorId = SelectedGame.Id,
            DescriptorVersion = SelectedGame.DescriptorVersion,
            RecipeId = SelectedGame.RecipeVersion is null ? null : SelectedGame.Id,
            RecipeVersion = SelectedGame.RecipeVersion,
            InstanceParamsJson = string.IsNullOrWhiteSpace(NewParamsJson) ? "{}" : NewParamsJson
        }, JsonSerializerOptions.Web);

        try
        {
            await _client.StoreAsync("server-instances", body, CancellationToken.None);
            CreateStatus = $"created '{id}' on {SelectedNode}";
            NewName = string.Empty;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            CreateStatus = $"error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RemoveAsync()
    {
        if (SelectedServer is null)
        {
            ActionStatus = "select a server first";
            return;
        }

        try
        {
            await _client.RemoveServerInstanceAsync(SelectedServer.Id, CancellationToken.None);
            ActionStatus = $"remove dispatched for {SelectedServer.Id}";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ActionStatus = $"error: {ex.Message}";
        }
    }

    // kind (button CommandParameter): "server-install" | "server-config-apply" | "recipe-apply".
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

    // A filesystem/systemd-safe instance id derived from the name (the {{instance.id}} token).
    private static string Slug(string name)
    {
        StringBuilder builder = new StringBuilder();
        foreach (char c in name.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(c) ? c : '-');
        }

        return builder.ToString().Trim('-');
    }

    private static string Short(string value) => value.Length > 80 ? value[..80] : value;
}
