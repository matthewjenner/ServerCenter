using Grpc.Core;
using ServerCenter.Contracts.V1;
using ServerCenter.Controller.Services;

namespace ServerCenter.Controller.Grpc;

// The operator-facing fleet endpoint the dashboard reads. Not client-cert authenticated (the UI
// uses operator auth, deferred). WatchFleet pushes a fresh snapshot on a cadence so the UI reads
// a stream rather than polling.
public sealed class FleetService(FleetSnapshotBuilder builder) : FleetView.FleetViewBase
{
    private static readonly TimeSpan PushInterval = TimeSpan.FromSeconds(2);

    public override Task<FleetSnapshot> GetFleet(GetFleetRequest request, ServerCallContext context) =>
        builder.BuildAsync(context.CancellationToken);

    public override async Task WatchFleet(
        WatchFleetRequest request,
        IServerStreamWriter<FleetSnapshot> responseStream,
        ServerCallContext context)
    {
        CancellationToken ct = context.CancellationToken;
        while (!ct.IsCancellationRequested)
        {
            await responseStream.WriteAsync(await builder.BuildAsync(ct), ct);
            try
            {
                await Task.Delay(PushInterval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
