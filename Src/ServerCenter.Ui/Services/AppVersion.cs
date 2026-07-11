using System.Reflection;

namespace ServerCenter.Ui.Services;

// The UI's own product version (from VersionPrefix), for the title bar.
public static class AppVersion
{
    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        string informational = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        int plus = informational.IndexOf('+');
        return plus >= 0 ? informational[..plus] : informational;
    }
}
