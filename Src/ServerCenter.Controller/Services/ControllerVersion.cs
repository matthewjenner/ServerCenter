using System.Reflection;

namespace ServerCenter.Controller.Services;

// The controller's own product version (from VersionPrefix), advertised to agents for self-update
// and shown in the fleet view. The three tiers ship in lockstep, so this is also the agent target.
public static class ControllerVersion
{
    // Clean semver from the assembly's informational version (strip any "+build" metadata).
    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        string informational = typeof(ControllerVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        int plus = informational.IndexOf('+');
        return plus >= 0 ? informational[..plus] : informational;
    }
}
