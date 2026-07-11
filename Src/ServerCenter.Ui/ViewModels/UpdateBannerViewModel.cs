using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerCenter.Ui.Services;

namespace ServerCenter.Ui.ViewModels;

// A thin banner over UpdateService: shows when a newer UI release is available and offers
// install/skip. Parameterless-constructible so it stays disabled (never shows) in tests and any
// context without an update service.
public sealed partial class UpdateBannerViewModel : ObservableObject
{
    private readonly UpdateService? _service;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAvailable))]
    [NotifyPropertyChangedFor(nameof(Message))]
    private string? _availableVersion;

    public UpdateBannerViewModel()
    {
    }

    public UpdateBannerViewModel(UpdateService service)
    {
        _service = service;
        AvailableVersion = service.AvailableVersion;
        service.UpdateAvailableChanged += version => AvailableVersion = version;
    }

    public bool IsAvailable => !string.IsNullOrEmpty(AvailableVersion);

    // Whether the running app can actually apply an update (false during dotnet run / not installed).
    public bool CanInstall => _service?.CanInstall ?? false;

    public string Message => IsAvailable
        ? (CanInstall
            ? $"ServerCenter {AvailableVersion} is available."
            : $"ServerCenter {AvailableVersion} is available (install the packaged app to auto-update).")
        : string.Empty;

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (_service is not null)
        {
            await _service.InstallAndRestartAsync();
        }
    }

    [RelayCommand]
    private void Skip() => _service?.SkipCurrentVersion();
}
