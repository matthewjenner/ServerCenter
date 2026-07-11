using System;
using System.Collections.ObjectModel;
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
    private IAdminClient? _admin;

    // Enrollment token minting.
    [ObservableProperty] private string _newNodeName = string.Empty;
    [ObservableProperty] private int _tokenTtlMinutes = 60;
    [ObservableProperty] private string _mintedToken = string.Empty;
    [ObservableProperty] private string _mintStatus = string.Empty;

    // Update policies ("profiles").
    [ObservableProperty] private string _policyJson = string.Empty;
    [ObservableProperty] private string _policyStatus = string.Empty;

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
            MintStatus = "connect to a controller first";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewNodeName))
        {
            MintStatus = "enter a node name";
            return;
        }

        try
        {
            EnrollmentTokenResult result = await _admin.MintEnrollmentTokenAsync(NewNodeName.Trim(), TokenTtlMinutes, CancellationToken.None);
            MintedToken = result.Token;
            MintStatus = $"token for '{result.DisplayName}' - expires in ~{TokenTtlMinutes} min. Copy it now; it is shown once.";
        }
        catch (Exception ex)
        {
            MintedToken = string.Empty;
            MintStatus = $"error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task StorePolicyAsync()
    {
        if (_admin is null)
        {
            PolicyStatus = "connect to a controller first";
            return;
        }

        if (string.IsNullOrWhiteSpace(PolicyJson))
        {
            PolicyStatus = "paste a policy definition first";
            return;
        }

        try
        {
            string response = await _admin.StoreAsync("update-policies", PolicyJson, CancellationToken.None);
            PolicyStatus = $"stored: {Short(response)}";
            await RefreshPoliciesAsync();
        }
        catch (Exception ex)
        {
            PolicyStatus = $"error: {ex.Message}";
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
            IReadOnlyList<string> ids = await _admin.ListPolicyIdsAsync(CancellationToken.None);
            Policies.Clear();
            foreach (string id in ids)
            {
                Policies.Add(id);
            }
        }
        catch
        {
            // best effort - leave the list as-is
        }
    }

    private static string Short(string value) => value.Length > 80 ? value[..80] : value;
}
