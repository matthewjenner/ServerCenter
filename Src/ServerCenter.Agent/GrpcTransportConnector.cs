using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Grpc.Net.Client;
using ServerCenter.Contracts.V1;
using ServerCenter.Core.Transport;

namespace ServerCenter.Agent;

// Dials the controller and opens the AgentLink bidi stream. This is the connect delegate
// AgentConnection calls on every (re)connect. A fresh channel per attempt keeps reconnect
// simple; the returned transport owns and disposes it. With TLS material it presents the agent's
// client cert (mTLS) and validates the server by chaining to the CA; without it, plaintext h2c.
public sealed class GrpcTransportConnector(string address, AgentTlsMaterial? tls = null)
{
    public Task<IAgentTransport> ConnectAsync(CancellationToken ct)
    {
        var channel = tls is null
            ? GrpcChannel.ForAddress(address)
            : GrpcChannel.ForAddress(address, new GrpcChannelOptions { HttpHandler = CreateTlsHandler(tls) });

        var call = new AgentLink.AgentLinkClient(channel).Connect(cancellationToken: ct);
        return Task.FromResult<IAgentTransport>(new GrpcAgentTransport(channel, call));
    }

    private static SocketsHttpHandler CreateTlsHandler(AgentTlsMaterial tls) => new()
    {
        SslOptions = new SslClientAuthenticationOptions
        {
            ClientCertificates = new X509CertificateCollection { tls.ClientCertificate },
            RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                certificate is X509Certificate2 server && ChainsToCa(server, tls.CaCertificate)
        }
    };

    private static bool ChainsToCa(X509Certificate2 presented, X509Certificate2 ca)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(presented);
    }
}

public sealed record AgentTlsMaterial(X509Certificate2 ClientCertificate, X509Certificate2 CaCertificate);
