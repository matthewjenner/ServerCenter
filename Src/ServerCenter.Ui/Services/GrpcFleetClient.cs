using System.Runtime.CompilerServices;
using Grpc.Core;
using Grpc.Net.Client;
using ServerCenter.Contracts.V1;

namespace ServerCenter.Ui.Services;

// Streams fleet snapshots from the controller's FleetView.WatchFleet.
public sealed class GrpcFleetClient(string address) : IFleetClient
{
    public async IAsyncEnumerable<FleetSnapshot> Watch([EnumeratorCancellation] CancellationToken ct)
    {
        using GrpcChannel channel = GrpcChannels.Create(address);
        FleetView.FleetViewClient client = new FleetView.FleetViewClient(channel);
        using AsyncServerStreamingCall<FleetSnapshot> call = client.WatchFleet(new WatchFleetRequest(), cancellationToken: ct);

        while (await call.ResponseStream.MoveNext(ct))
        {
            yield return call.ResponseStream.Current;
        }
    }
}
