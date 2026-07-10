using System.Text;
using ServerCenter.Core.Recipes;

namespace ServerCenter.Agent.Linux;

// Renders a recipe's ServiceDefinition into a systemd unit file. Pure and directly unit-testable;
// uses explicit "\n" (systemd unit, not platform newlines) so the output is correct regardless of
// where the render runs.
public static class SystemdUnitRenderer
{
    public static string Render(ServiceDefinition service)
    {
        StringBuilder unit = new StringBuilder();
        unit.Append("[Unit]\n");
        unit.Append($"Description={service.Unit}\n\n");
        unit.Append("[Service]\n");
        unit.Append($"ExecStart={service.ExecStart}\n");
        if (!string.IsNullOrEmpty(service.User))
        {
            unit.Append($"User={service.User}\n");
        }

        unit.Append($"Restart={service.Restart}\n\n");
        unit.Append("[Install]\n");
        unit.Append("WantedBy=multi-user.target\n");
        return unit.ToString();
    }
}
