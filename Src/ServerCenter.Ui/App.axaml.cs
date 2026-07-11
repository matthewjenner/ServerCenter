using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ServerCenter.Ui.Services;
using ServerCenter.Ui.ViewModels;

namespace ServerCenter.Ui;

public partial class App : Application
{
    private MainWindowViewModel? _main;
    private UpdateService? _updates;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ConnectionSettings settings = new ConnectionSettings();
            string startAddress = settings.ResolveStartupAddress();
            DashboardViewModel fleet = new DashboardViewModel();
            JobsViewModel jobs = new JobsViewModel(new GrpcJobClient(startAddress));
            ServersViewModel servers = new ServersViewModel(new HttpAdminClient(startAddress));
            SettingsViewModel settingsTab = new SettingsViewModel();

            _updates = new UpdateService();
            UpdateBannerViewModel updateBanner = new UpdateBannerViewModel(_updates);

            _main = new MainWindowViewModel(fleet, jobs, servers, settingsTab, CreateClients, settings, updateBanner);
            _main.ConnectCommand.Execute(null);   // initial connect using the saved/env/default address

            MainWindow window = new MainWindow { DataContext = _main };
            window.AttachSettings(settings);   // restore saved size/position/tab, save them on close
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) =>
            {
                _main.Dispose();
                _updates?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static (IFleetClient Fleet, IJobClient Jobs, IAdminClient Admin) CreateClients(string address) =>
        (new GrpcFleetClient(address), new GrpcJobClient(address), new HttpAdminClient(address));
}
