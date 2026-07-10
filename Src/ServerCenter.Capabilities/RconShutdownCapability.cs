using ServerCenter.Core.Capabilities;
using ServerCenter.Core.Games;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Primitives;

namespace ServerCenter.Capabilities;

// Graceful shutdown via RCON: announce/drain with the descriptor's drain command, then wait out the
// grace period so players can leave before the service is stopped. The service stop itself is the
// executor's job (service control) - this capability is only the graceful drain. The clock is
// injected so the grace wait is deterministic in tests.
public sealed class RconShutdownCapability(ShutdownSpec spec, IRconClient rcon, TimeProvider clock) : IShutdownCapability
{
    public CapabilityKind Kind => CapabilityKind.Shutdown;

    public async Task GracefulShutdownAsync(ShutdownContext ctx, IJobSink sink, CancellationToken ct)
    {
        RconEndpoint endpoint = RconEndpoints.From(ctx.InstanceParams);
        await using IRconSession session = await rcon.ConnectAsync(endpoint, ct);

        sink.Log(LogStream.Note, $"draining: {spec.DrainCommand}");
        await session.ExecuteAsync(spec.DrainCommand, ct);

        // The caller's grace overrides the descriptor default when set.
        int graceSeconds = ctx.GraceSeconds > 0 ? ctx.GraceSeconds : spec.GraceSeconds;
        if (graceSeconds > 0)
        {
            sink.Log(LogStream.Note, $"grace period {graceSeconds}s");
            await Task.Delay(TimeSpan.FromSeconds(graceSeconds), clock, ct);
        }
    }
}
