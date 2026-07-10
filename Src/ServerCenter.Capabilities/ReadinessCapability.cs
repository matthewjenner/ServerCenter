using ServerCenter.Core.Capabilities;
using ServerCenter.Core.Games;
using ServerCenter.Core.Primitives;
using ServerCenter.Primitives.ConfigTemplating;

namespace ServerCenter.Capabilities;

// Readiness = game-level "accepting players", NOT process-alive (brief trap). The descriptor's port
// is a token ("{{ports.game}}") resolved from instance params, then probed. Only port-probe is
// implemented today (the most universal signal - the game port is actually listening); query-protocol
// (a2s) and log-scrape land when needed, and this fails loudly rather than pretending for them.
public sealed class ReadinessCapability(ReadinessSpec spec, IPortProbe portProbe) : IReadinessCapability
{
    public CapabilityKind Kind => CapabilityKind.Readiness;

    public async Task<Readiness> ProbeAsync(ReadinessContext ctx, CancellationToken ct)
    {
        if (spec.Primitive != "port-probe")
        {
            throw new NotSupportedException(
                $"readiness primitive '{spec.Primitive}' is not supported yet (only port-probe)");
        }

        string host = ctx.InstanceParams.GetValueOrDefault("readiness.host", "127.0.0.1");
        string portText = ConfigTemplateRenderer.Render(spec.Port, ctx.InstanceParams);
        if (!int.TryParse(portText, out int port))
        {
            throw new InvalidOperationException($"readiness port '{spec.Port}' resolved to '{portText}', not a number");
        }

        return await portProbe.IsOpenAsync(host, port, ct)
            ? Readiness.AcceptingPlayers
            : Readiness.Down;
    }
}
