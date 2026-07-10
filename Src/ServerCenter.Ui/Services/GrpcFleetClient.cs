using System.Runtime.CompilerServices;
using Grpc.Core;
using ServerCenter.Contracts.V1;

namespace ServerCenter.Ui.Services;

// Streams fleet snapshots from the controller's FleetView.WatchFleet.
public sealed class GrpcFleetClient(string address) : IFleetClient
{
    public async IAsyncEnumerable<FleetSnapshot> Watch([EnumeratorCancellation] CancellationToken ct)
    {
        using var channel = GrpcChannels.Create(address);
        var client = new FleetView.FleetViewClient(channel);
        using var call = client.WatchFleet(new WatchFleetRequest(), cancellationToken: ct);

        while (await call.ResponseStream.MoveNext(ct))
        {
            yield return call.ResponseStream.Current;
        }
    }
}
