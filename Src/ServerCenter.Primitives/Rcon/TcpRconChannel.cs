using System.Buffers.Binary;
using System.Net.Sockets;
using ServerCenter.Core.Primitives;

namespace ServerCenter.Primitives.Rcon;

// The real RCON channel: length-prefixed Source RCON framing over TCP. Thin by design - all the
// sequencing lives in SourceRconSession; this only reads/writes frames. Smoked at Tier 2 against a
// real dedicated-server process.
public sealed class TcpRconChannelFactory : IRconChannelFactory
{
    public async Task<IRconChannel> OpenAsync(RconEndpoint endpoint, CancellationToken ct)
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(endpoint.Host, endpoint.Port, ct);
            return new TcpRconChannel(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}

internal sealed class TcpRconChannel(TcpClient client) : IRconChannel
{
    private readonly NetworkStream _stream = client.GetStream();

    public Task SendAsync(RconPacket packet, CancellationToken ct) =>
        _stream.WriteAsync(RconProtocol.Encode(packet), ct).AsTask();

    public async Task<RconPacket> ReceiveAsync(CancellationToken ct)
    {
        var prefix = new byte[4];
        await _stream.ReadExactlyAsync(prefix, ct);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is < 10 or > RconProtocol.MaxContentBytes)
        {
            throw new InvalidDataException($"RCON frame length {length} out of range");
        }

        var content = new byte[length];
        await _stream.ReadExactlyAsync(content, ct);
        return RconProtocol.Decode(content);
    }

    public ValueTask DisposeAsync()
    {
        _stream.Dispose();
        client.Dispose();
        return ValueTask.CompletedTask;
    }
}
