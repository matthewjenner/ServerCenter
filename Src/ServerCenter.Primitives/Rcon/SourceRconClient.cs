using System.Text;
using ServerCenter.Core.Primitives;

namespace ServerCenter.Primitives.Rcon;

// The Source RCON client (brief: RCON is the highest-leverage primitive - build early and solid).
// Powers stats, graceful shutdown, quiesce-before-backup, drain-before-host-reboot, admin actions.
// The channel seam keeps the sequencing logic testable; TcpRconChannel provides the real framing.
public sealed class SourceRconClient(IRconChannelFactory channels) : IRconClient
{
    public async Task<IRconSession> ConnectAsync(RconEndpoint endpoint, CancellationToken ct)
    {
        var channel = await channels.OpenAsync(endpoint, ct);
        try
        {
            var session = new SourceRconSession(channel);
            await session.AuthenticateAsync(endpoint.Password, ct);
            return session;
        }
        catch
        {
            await channel.DisposeAsync();
            throw;
        }
    }
}

internal sealed class SourceRconSession(IRconChannel channel) : IRconSession
{
    private int _nextId;

    // Auth: send the password as a SERVERDATA_AUTH; the server replies with an (ignored) empty
    // RESPONSE_VALUE then a SERVERDATA_AUTH_RESPONSE. A response id of -1 (or not matching the
    // request) means the password was rejected.
    public async Task AuthenticateAsync(string password, CancellationToken ct)
    {
        var id = NextId();
        await channel.SendAsync(new RconPacket(id, RconPacketTypes.Auth, password), ct);

        while (true)
        {
            var reply = await channel.ReceiveAsync(ct);
            if (reply.Type != RconPacketTypes.AuthResponse)
            {
                continue; // the pre-auth junk RESPONSE_VALUE some servers send
            }

            if (reply.Id == RconPacketTypes.AuthFailureId || reply.Id != id)
            {
                throw new RconAuthenticationException("RCON authentication was rejected (bad password).");
            }

            return;
        }
    }

    public async Task<string> ExecuteAsync(string command, CancellationToken ct)
    {
        var commandId = NextId();
        var sentinelId = NextId();

        await channel.SendAsync(new RconPacket(commandId, RconPacketTypes.ExecCommand, command), ct);
        // Empty follow-up packet: the server echoes it AFTER every real response packet, marking the
        // end of a possibly multi-packet response (Valve's documented multi-packet technique).
        await channel.SendAsync(new RconPacket(sentinelId, RconPacketTypes.ResponseValue, string.Empty), ct);

        var body = new StringBuilder();
        while (true)
        {
            var reply = await channel.ReceiveAsync(ct);
            if (reply.Id == sentinelId)
            {
                break;
            }

            if (reply.Id == commandId)
            {
                body.Append(reply.Body);
            }
        }

        return body.ToString();
    }

    public ValueTask DisposeAsync() => channel.DisposeAsync();

    // Positive, monotonic ids (never -1, which is the auth-failure sentinel).
    private int NextId() => Interlocked.Increment(ref _nextId);
}
