using System.Net.Security;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Grpc.Net.Client;
using ServerCenter.Contracts.V1;

namespace ServerCenter.Ui.Services;

// Streams fleet snapshots from the controller's FleetView.WatchFleet. Over https it validates
// the server on a trust-on-first-use basis for now (operator auth + proper CA validation land
// later); over http it uses plaintext h2c for dev.
public sealed class GrpcFleetClient(string address) : IFleetClient
{
    public async IAsyncEnumerable<FleetSnapshot> Watch([EnumeratorCancellation] CancellationToken ct)
    {
        using var channel = CreateChannel();
        var client = new FleetView.FleetViewClient(channel);
        using var call = client.WatchFleet(new WatchFleetRequest(), cancellationToken: ct);

        while (await call.ResponseStream.MoveNext(ct))
        {
            yield return call.ResponseStream.Current;
        }
    }

    private GrpcChannel CreateChannel()
    {
        if (address.StartsWith("https", StringComparison.OrdinalIgnoreCase))
        {
            var handler = new SocketsHttpHandler
            {
                SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = (_, _, _, _) => true }
            };
            return GrpcChannel.ForAddress(address, new GrpcChannelOptions { HttpHandler = handler });
        }

        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        return GrpcChannel.ForAddress(address);
    }
}
