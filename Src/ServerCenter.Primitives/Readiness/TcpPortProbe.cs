using System.Net.Sockets;
using ServerCenter.Core.Primitives;

namespace ServerCenter.Primitives.Readiness;

// Real readiness port-probe: a TCP connect attempt. A refused/unreachable port (the game not yet
// listening) is "not open", not an error. Smoked at Tier 2 against a real dedicated server.
public sealed class TcpPortProbe : IPortProbe
{
    public async Task<bool> IsOpenAsync(string host, int port, CancellationToken ct)
    {
        try
        {
            using TcpClient client = new TcpClient();
            await client.ConnectAsync(host, port, ct);
            return client.Connected;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
