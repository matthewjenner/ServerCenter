using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ServerCenter.Ui.Services;
using ServerCenter.Ui.ViewModels;

namespace ServerCenter.Ui;

public partial class App : Application
{
    private CancellationTokenSource? _cts;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string address = Environment.GetEnvironmentVariable("SERVERCENTER_CONTROLLER") ?? "https://localhost:5443";

            DashboardViewModel fleet = new DashboardViewModel();
            JobsViewModel jobs = new JobsViewModel(new GrpcJobClient(address));

            _cts = new CancellationTokenSource();
            _ = fleet.RunAsync(new GrpcFleetClient(address), _cts.Token);
            _ = jobs.RunAsync(_cts.Token);

            desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel(fleet, jobs) };
            desktop.ShutdownRequested += (_, _) => _cts.Cancel();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
