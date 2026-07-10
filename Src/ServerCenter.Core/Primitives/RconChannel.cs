namespace ServerCenter.Core.Primitives;

// The packet-level seam under IRconClient. The Source RCON sequencing logic (auth handshake,
// multi-packet response accumulation) runs over this, so it is Tier 1 testable against a fake
// channel; the real byte framing (a thin length-prefixed adapter over TCP) is smoked at Tier 2.
public interface IRconChannelFactory
{
    Task<IRconChannel> OpenAsync(RconEndpoint endpoint, CancellationToken ct);
}

public interface IRconChannel : IAsyncDisposable
{
    Task SendAsync(RconPacket packet, CancellationToken ct);

    Task<RconPacket> ReceiveAsync(CancellationToken ct);
}

public sealed record RconPacket(int Id, int Type, string Body);

// Source RCON packet types. ExecCommand and AuthResponse share the value 2 (they are
// direction-disambiguated on the wire): the client only ever SENDS Auth/ExecCommand/ResponseValue,
// and only ever RECEIVES ResponseValue/AuthResponse.
public static class RconPacketTypes
{
    public const int ResponseValue = 0;
    public const int ExecCommand = 2;
    public const int AuthResponse = 2;
    public const int Auth = 3;

    // An auth response echoing this id (instead of the request id) signals a rejected password.
    public const int AuthFailureId = -1;
}

public sealed class RconAuthenticationException(string message) : Exception(message);
