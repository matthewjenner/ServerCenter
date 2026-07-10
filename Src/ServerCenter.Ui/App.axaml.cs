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
            DashboardViewModel fleet = new DashboardViewModel();
            JobsViewModel jobs = new JobsViewModel(new GrpcJobClient(settings.ResolveStartupAddress()));

            _main = new MainWindowViewModel(fleet, jobs, CreateClients, settings);
            _main.ConnectCommand.Execute(null);   // initial connect using the saved/env/default address

            desktop.MainWindow = new MainWindow { DataContext = _main };
            desktop.ShutdownRequested += (_, _) => _main.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static (IFleetClient Fleet, IJobClient Jobs) CreateClients(string address) =>
        (new GrpcFleetClient(address), new GrpcJobClient(address));
}
