using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ServerCenter.Ui.Services;
using ServerCenter.Ui.ViewModels;

namespace ServerCenter.Ui;

public partial class App : Application
{
    private MainWindowViewModel? _main;

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

            _main = new MainWindowViewModel(fleet, jobs, servers, CreateClients, settings);
            _main.ConnectCommand.Execute(null);   // initial connect using the saved/env/default address

            desktop.MainWindow = new MainWindow { DataContext = _main };
            desktop.ShutdownRequested += (_, _) => _main.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static (IFleetClient Fleet, IJobClient Jobs, IAdminClient Admin) CreateClients(string address) =>
        (new GrpcFleetClient(address), new GrpcJobClient(address), new HttpAdminClient(address));
}
