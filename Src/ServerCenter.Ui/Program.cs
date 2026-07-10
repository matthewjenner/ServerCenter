using Avalonia;
using Velopack;

namespace ServerCenter.Ui;

// Thin Avalonia view onto the controller (brief: UI talks only to the controller). The live
// dual-truth dashboard lands in Phase 2; this is the app shell.
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must be the first call in Main, exactly once. Handles Velopack install/update/
        // uninstall hook events and exits before Avalonia starts when invoked for those.
        // No-op during dev (dotnet run). The update-check UI lands with the Phase 2 dashboard.
        VelopackApp.Build().Run();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
