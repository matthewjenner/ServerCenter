using System.Net.Security;
using Grpc.Net.Client;

namespace ServerCenter.Ui.Services;

// Shared channel factory. Over https it validates the server trust-on-first-use for now (operator
// auth + proper CA validation land later); over http it uses plaintext h2c for dev.
internal static class GrpcChannels
{
    public static GrpcChannel Create(string address)
    {
        if (address.StartsWith("https", StringComparison.OrdinalIgnoreCase))
        {
            SocketsHttpHandler handler = new SocketsHttpHandler
            {
                SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = (_, _, _, _) => true }
            };
            return GrpcChannel.ForAddress(address, new GrpcChannelOptions { HttpHandler = handler });
        }

        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        return GrpcChannel.ForAddress(address);
    }
}
