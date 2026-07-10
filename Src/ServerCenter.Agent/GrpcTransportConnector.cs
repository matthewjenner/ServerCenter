using Grpc.Net.Client;
using ServerCenter.Contracts.V1;
using ServerCenter.Core.Transport;

namespace ServerCenter.Agent;

// Dials the controller and opens the AgentLink bidi stream. This is the connect delegate
// AgentConnection calls on every (re)connect. A fresh channel per attempt keeps reconnect
// simple; the returned transport owns and disposes it. TLS/mTLS replaces the plaintext dial
// in a later Phase 1 ship (brief 3.8).
public sealed class GrpcTransportConnector(string address)
{
    public Task<IAgentTransport> ConnectAsync(CancellationToken ct)
    {
        var channel = GrpcChannel.ForAddress(address);
        var call = new AgentLink.AgentLinkClient(channel).Connect(cancellationToken: ct);
        return Task.FromResult<IAgentTransport>(new GrpcAgentTransport(channel, call));
    }
}
