using ServerCenter.Core.Capabilities;
using ServerCenter.Core.Games;
using ServerCenter.Core.Primitives;

namespace ServerCenter.Capabilities;

// Stats via RCON: run each declared command against the server and return the raw responses keyed by
// their logical name. Structured extraction (e.g. a real player count) is game-specific and layers
// on per descriptor later; today the capability surfaces the raw command output.
public sealed class RconStatsCapability(StatsSpec spec, IRconClient rcon) : IStatsCapability
{
    public CapabilityKind Kind => CapabilityKind.Stats;

    public async Task<ServerStats> ReadAsync(StatsContext ctx, CancellationToken ct)
    {
        var endpoint = RconEndpoints.From(ctx.InstanceParams);
        await using var session = await rcon.ConnectAsync(endpoint, ct);

        var raw = new Dictionary<string, string>();
        foreach (var (name, command) in spec.Commands)
        {
            raw[name] = await session.ExecuteAsync(command, ct);
        }

        return new ServerStats(PlayerCount: null, raw);
    }
}
