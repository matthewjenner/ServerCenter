using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerCenter.Ui.Services;

namespace ServerCenter.Ui.ViewModels;

// The Settings tab: app-wide config and operator setup that isn't per-node. Today it hosts (1) the
// enrollment-token mint (seed a new node's trust out-of-band) and (2) update policies - the reusable
// "update profiles" the fleet cards pick from. The connection controls live here too, bound to the
// root view-model. The admin client is swapped on reconnect.
//
// The policy editor is STRUCTURED (dropdowns for the strict enums, text for the free fields, checkboxes
// for preflight) rather than freeform JSON: an UpdatePolicy has a fixed shape, so the fields both
// prevent typos and document the options. A live read-only JSON preview shows exactly what will be
// stored (the canonical kebab-case dialect), and Store auto-bumps the version to a new revision.
public sealed partial class SettingsViewModel : ObservableObject
{
    private IAdminClient? _admin;
    private readonly Dictionary<string, string> _policyBodies = new(StringComparer.OrdinalIgnoreCase);

    // Enrollment token minting. TTL is decimal? so the NumericUpDown can be cleared without a binding
    // cast error; the command falls back to 60 when it is empty.
    [ObservableProperty] private string _newNodeName = string.Empty;
    [ObservableProperty] private decimal? _tokenTtlMinutes = 60;
    [ObservableProperty] private string _mintedToken = string.Empty;
    [ObservableProperty] private string _mintStatus = string.Empty;

    // Update-policy editor fields. Values are the canonical WIRE tokens (kebab-case), so the dropdown
    // selection is stored verbatim - no display-to-wire mapping to drift.
    [ObservableProperty] private string _policyId = string.Empty;
    [ObservableProperty] private string _policyProvider = "apt";
    [ObservableProperty] private string _policyHow = "in-place";
    [ObservableProperty] private string _policyServiceUnit = string.Empty;
    [ObservableProperty] private string _policyWhenMode = "manual";
    [ObservableProperty] private string _policyCron = string.Empty;
    [ObservableProperty] private decimal? _policyWindowMinutes;
    [ObservableProperty] private string _policyReboot = "if-required";
    [ObservableProperty] private string _policyApproval = "auto";
    [ObservableProperty] private bool _preflightNotify = true;
    [ObservableProperty] private bool _preflightSnapshotFirst;
    [ObservableProperty] private bool _preflightDrainPlayers;
    [ObservableProperty] private bool _preflightQuiesce;

    [ObservableProperty] private string _policyPreview = string.Empty;
    [ObservableProperty] private string _policyStatus = string.Empty;

    // Dropdown option lists (the wire tokens). Provider is open-ended per the contract, so it is a
    // mutable list a loaded policy can extend; the rest are fixed enums that fail to deserialize if wrong.
    public ObservableCollection<string> ProviderOptions { get; } = ["apt", "plex", "steamcmd", "wu"];

    public IReadOnlyList<string> HowOptions { get; } = ["in-place", "stop-update-start", "drain-then-update"];

    public IReadOnlyList<string> WhenModeOptions { get; } = ["manual", "window"];

    public IReadOnlyList<string> RebootOptions { get; } = ["never", "if-required", "always-after", "prompt"];

    public IReadOnlyList<string> ApprovalOptions { get; } = ["auto", "require-confirmation"];

    // The ids of policies already defined on the controller; click one to load it into the editor.
    public ObservableCollection<string> Policies { get; } = [];

    // The service unit only matters when the update brackets a service; cron/window only when scheduled.
    // These gate the relevant fields in the view.
    public bool NeedsServiceUnit => PolicyHow is "stop-update-start" or "drain-then-update";

    public bool IsWindowed => PolicyWhenMode == "window";

    public SettingsViewModel() => UpdatePreview();

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

    // Reset the editor to a sensible starter policy (a manual apt profile) for authoring a new one.
    [RelayCommand]
    private void NewPolicy()
    {
        PolicyId = string.Empty;
        PolicyProvider = "apt";
        PolicyHow = "in-place";
        PolicyServiceUnit = string.Empty;
        PolicyWhenMode = "manual";
        PolicyCron = string.Empty;
        PolicyWindowMinutes = null;
        PolicyReboot = "if-required";
        PolicyApproval = "auto";
        PreflightNotify = true;
        PreflightSnapshotFirst = false;
        PreflightDrainPlayers = false;
        PreflightQuiesce = false;
        PolicyStatus = "New policy - name it, adjust the fields, then Store.";
    }

    // Load a defined policy into the fields so the operator can read or clone it. Storing then saves a
    // new revision (version auto-bumped).
    [RelayCommand]
    private void LoadPolicy(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || !_policyBodies.TryGetValue(id, out string? body))
        {
            return;
        }

        try
        {
            ApplyToFields(body);
            PolicyStatus = $"Loaded '{id}' - edit and Store to save a new revision.";
        }
        catch (JsonException ex)
        {
            PolicyStatus = $"Could not parse '{id}': {ex.Message}";
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

        if (string.IsNullOrWhiteSpace(PolicyId))
        {
            PolicyStatus = "Give the policy an id first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(PolicyProvider))
        {
            PolicyStatus = "Pick or enter a provider (apt, plex, steamcmd, ...).";
            return;
        }

        int version = NextVersionFor(PolicyId);
        try
        {
            string response = await _admin.StoreAsync("update-policies", BuildPolicyJson(version), CancellationToken.None);
            PolicyStatus = $"Stored '{PolicyId.Trim()}' v{version}: {Short(response)}";
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

    // The next version to store for an id: one past the latest already defined, or 1 for a new id. Keeps
    // Store from colliding on the (id, version) primary key and makes an edit a clean new revision.
    private int NextVersionFor(string id)
    {
        if (_policyBodies.TryGetValue(id.Trim(), out string? body))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("version", out JsonElement version) && version.TryGetInt32(out int current))
                {
                    return current + 1;
                }
            }
            catch (JsonException)
            {
                // fall through to 1
            }
        }

        return 1;
    }

    // Serialize the fields into the canonical policy dialect (camelCase properties, kebab-case enum
    // tokens, null/empty fields omitted). The dropdown values ARE the wire tokens, so no mapping.
    private string BuildPolicyJson(int version)
    {
        List<string> preflight = new List<string>();
        if (PreflightNotify)
        {
            preflight.Add("notify");
        }

        if (PreflightSnapshotFirst)
        {
            preflight.Add("snapshot-first");
        }

        if (PreflightDrainPlayers)
        {
            preflight.Add("drain-players-via-rcon");
        }

        if (PreflightQuiesce)
        {
            preflight.Add("quiesce");
        }

        Dictionary<string, object?> when = new Dictionary<string, object?> { ["mode"] = PolicyWhenMode };
        if (IsWindowed)
        {
            if (!string.IsNullOrWhiteSpace(PolicyCron))
            {
                when["cron"] = PolicyCron.Trim();
            }

            if (PolicyWindowMinutes is decimal minutes)
            {
                when["windowMinutes"] = (int)minutes;
            }
        }

        Dictionary<string, object?> policy = new Dictionary<string, object?>
        {
            ["id"] = PolicyId.Trim(),
            ["version"] = version,
            ["what"] = new Dictionary<string, object?> { ["provider"] = PolicyProvider.Trim() },
            ["how"] = PolicyHow,
            ["when"] = when,
            ["reboot"] = PolicyReboot,
            ["preflight"] = preflight,
            ["approval"] = PolicyApproval
        };

        if (NeedsServiceUnit && !string.IsNullOrWhiteSpace(PolicyServiceUnit))
        {
            policy["serviceUnit"] = PolicyServiceUnit.Trim();
        }

        return JsonSerializer.Serialize(policy, new JsonSerializerOptions { WriteIndented = true });
    }

    // Parse a stored policy body into the editor fields (the inverse of BuildPolicyJson).
    private void ApplyToFields(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        PolicyId = GetString(root, "id");
        PolicyProvider = root.TryGetProperty("what", out JsonElement what) && what.TryGetProperty("provider", out JsonElement provider)
            ? provider.GetString() ?? "apt"
            : "apt";
        EnsureProviderOption(PolicyProvider);
        PolicyHow = GetString(root, "how", "in-place");
        PolicyServiceUnit = GetString(root, "serviceUnit");
        PolicyReboot = GetString(root, "reboot", "if-required");
        PolicyApproval = GetString(root, "approval", "auto");

        PolicyWhenMode = "manual";
        PolicyCron = string.Empty;
        PolicyWindowMinutes = null;
        if (root.TryGetProperty("when", out JsonElement when))
        {
            PolicyWhenMode = when.TryGetProperty("mode", out JsonElement mode) ? mode.GetString() ?? "manual" : "manual";
            if (when.TryGetProperty("cron", out JsonElement cron))
            {
                PolicyCron = cron.GetString() ?? string.Empty;
            }

            if (when.TryGetProperty("windowMinutes", out JsonElement windowMinutes) && windowMinutes.TryGetInt32(out int minutes))
            {
                PolicyWindowMinutes = minutes;
            }
        }

        PreflightNotify = false;
        PreflightSnapshotFirst = false;
        PreflightDrainPlayers = false;
        PreflightQuiesce = false;
        if (root.TryGetProperty("preflight", out JsonElement preflight) && preflight.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement step in preflight.EnumerateArray())
            {
                switch (step.GetString())
                {
                    case "notify": PreflightNotify = true; break;
                    case "snapshot-first": PreflightSnapshotFirst = true; break;
                    case "drain-players-via-rcon": PreflightDrainPlayers = true; break;
                    case "quiesce": PreflightQuiesce = true; break;
                }
            }
        }
    }

    // Keep a loaded policy's provider selectable even if it is not one of the built-in suggestions.
    private void EnsureProviderOption(string provider)
    {
        if (!string.IsNullOrWhiteSpace(provider) && !ProviderOptions.Contains(provider))
        {
            ProviderOptions.Add(provider);
        }
    }

    private void UpdatePreview() => PolicyPreview = BuildPolicyJson(NextVersionFor(PolicyId));

    // Any editor field change refreshes the live preview; a couple also toggle a gated field.
    partial void OnPolicyIdChanged(string value) => UpdatePreview();

    partial void OnPolicyProviderChanged(string value) => UpdatePreview();

    partial void OnPolicyHowChanged(string value)
    {
        OnPropertyChanged(nameof(NeedsServiceUnit));
        UpdatePreview();
    }

    partial void OnPolicyServiceUnitChanged(string value) => UpdatePreview();

    partial void OnPolicyWhenModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsWindowed));
        UpdatePreview();
    }

    partial void OnPolicyCronChanged(string value) => UpdatePreview();

    partial void OnPolicyWindowMinutesChanged(decimal? value) => UpdatePreview();

    partial void OnPolicyRebootChanged(string value) => UpdatePreview();

    partial void OnPolicyApprovalChanged(string value) => UpdatePreview();

    partial void OnPreflightNotifyChanged(bool value) => UpdatePreview();

    partial void OnPreflightSnapshotFirstChanged(bool value) => UpdatePreview();

    partial void OnPreflightDrainPlayersChanged(bool value) => UpdatePreview();

    partial void OnPreflightQuiesceChanged(bool value) => UpdatePreview();

    private static string GetString(JsonElement root, string name, string fallback = "") =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static string Short(string value) => value.Length > 80 ? value[..80] : value;
}
