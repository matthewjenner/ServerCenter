using ServerCenter.Core.Primitives;

namespace ServerCenter.Capabilities;

// Resolves an RCON endpoint from a server instance's flattened params. The agent runs ON the game
// node, so the host defaults to loopback; the port and password are instance params
// (ports.rcon / rcon.password). Missing required params fail loudly rather than dialing a wrong port.
public static class RconEndpoints
{
    public static RconEndpoint From(IReadOnlyDictionary<string, string> instanceParams)
    {
        string host = instanceParams.GetValueOrDefault("rcon.host", "127.0.0.1");

        if (!instanceParams.TryGetValue("ports.rcon", out string? portText) || !int.TryParse(portText, out int port))
        {
            throw new InvalidOperationException("instance params are missing a valid 'ports.rcon'");
        }

        if (!instanceParams.TryGetValue("rcon.password", out string? password))
        {
            throw new InvalidOperationException("instance params are missing 'rcon.password'");
        }

        return new RconEndpoint(host, port, password);
    }
}
