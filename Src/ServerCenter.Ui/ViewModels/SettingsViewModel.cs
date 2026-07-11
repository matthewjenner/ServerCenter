using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerCenter.Ui.Services;

namespace ServerCenter.Ui.ViewModels;

// The Settings tab: app-wide config and operator setup that isn't per-node. Today it hosts (1) the
// enrollment-token mint (seed a new node's trust out-of-band) and (2) update policies - the reusable
// "update profiles" that the fleet cards pick from. The connection controls (controller address +
// Connect) live here too, bound to the root view-model. The admin client is swapped on reconnect.
public sealed partial class SettingsViewModel : ObservableObject
{
    // A starter update policy, in the canonical dialect, so an operator has a valid thing to edit
    // instead of a blank box. Mirrors the seeded "apt" default; see UpdatePolicy for every field's
    // options (surfaced in the field's tooltip too).
    private const string ExamplePolicyJson =
        "{\n" +
        "  \"id\": \"my-policy\",\n" +
        "  \"version\": 1,\n" +
        "  \"what\": { \"provider\": \"apt\" },\n" +
        "  \"how\": \"in-place\",\n" +
        "  \"when\": { \"mode\": \"manual\" },\n" +
        "  \"reboot\": \"if-required\",\n" +
        "  \"preflight\": [\"notify\"],\n" +
        "  \"approval\": \"auto\"\n" +
        "}";

    private IAdminClient? _admin;
    private readonly Dictionary<string, string> _policyBodies = new(StringComparer.OrdinalIgnoreCase);

    // Enrollment token minting. TTL is decimal? so the NumericUpDown can be cleared without a binding
    // cast error; the command falls back to 60 when it is empty.
    [ObservableProperty] private string _newNodeName = string.Empty;
    [ObservableProperty] private decimal? _tokenTtlMinutes = 60;
    [ObservableProperty] private string _mintedToken = string.Empty;
    [ObservableProperty] private string _mintStatus = string.Empty;

    // Update policies ("profiles").
    [ObservableProperty] private string _policyJson = string.Empty;
    [ObservableProperty] private string _policyStatus = string.Empty;

    // The ids of policies already defined on the controller; click one to load it into the editor.
    public ObservableCollection<string> Policies { get; } = [];

    public void UseClient(IAdminClient admin)
    {
        _admin = admin;
        _ = RefreshPoliciesAsync();
    }

    [RelayCommand]
    private async Task MintTokenAsync()
    {
        if (_admin is null)
        {
            MintStatus = "Connect to a controller first (Connection, above).";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewNodeName))
        {
            MintStatus = "Enter a name for the node first.";
            return;
        }

        int ttlMinutes = (int)Math.Clamp(TokenTtlMinutes ?? 60m, 1m, 24m * 60m);
        try
        {
            EnrollmentTokenResult result = await _admin.MintEnrollmentTokenAsync(NewNodeName.Trim(), ttlMinutes, CancellationToken.None);
            MintedToken = result.Token;
            MintStatus = $"Token for '{result.DisplayName}' - valid ~{ttlMinutes} min. Copy it now; it is shown only once.";
        }
        catch (Exception ex)
        {
            MintedToken = string.Empty;
            MintStatus = $"Could not mint a token: {ex.Message}";
        }
    }

    [RelayCommand]
    private void LoadExample() => PolicyJson = ExamplePolicyJson;

    // Load a defined policy into the editor so the operator can read or clone it instead of authoring
    // JSON from scratch.
    [RelayCommand]
    private void LoadPolicy(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id) && _policyBodies.TryGetValue(id, out string? body))
        {
            PolicyJson = Pretty(body);
            PolicyStatus = $"Loaded '{id}' - edit and Store to save a new revision.";
        }
    }

    [RelayCommand]
    private async Task StorePolicyAsync()
    {
        if (_admin is null)
        {
            PolicyStatus = "Connect to a controller first (Connection, above).";
            return;
        }

        if (string.IsNullOrWhiteSpace(PolicyJson))
        {
            PolicyStatus = "Nothing to store - paste a policy, or use Load example.";
            return;
        }

        try
        {
            string response = await _admin.StoreAsync("update-policies", PolicyJson, CancellationToken.None);
            PolicyStatus = $"Stored: {Short(response)}";
            await RefreshPoliciesAsync();
        }
        catch (Exception ex)
        {
            PolicyStatus = $"Could not store the policy: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshPoliciesAsync()
    {
        if (_admin is null)
        {
            return;
        }

        try
        {
            IReadOnlyList<PolicyDoc> docs = await _admin.ListPoliciesAsync(CancellationToken.None);
            _policyBodies.Clear();
            Policies.Clear();
            foreach (PolicyDoc doc in docs)
            {
                _policyBodies[doc.Id] = doc.Json;
                Policies.Add(doc.Id);
            }
        }
        catch
        {
            // best effort - leave the list as-is
        }
    }

    // Pretty-print a compact policy body for the editor; if it is not valid JSON, show it as-is.
    private static string Pretty(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static string Short(string value) => value.Length > 80 ? value[..80] : value;
}
