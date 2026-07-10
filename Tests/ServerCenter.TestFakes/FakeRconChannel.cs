using ServerCenter.Core.Primitives;

namespace ServerCenter.TestFakes;

// A scripted Source RCON server behind the channel seam: it validates the auth password and answers
// commands from a map (a command can map to several response parts, exercising the multi-packet
// accumulation path). No sockets - the RCON client's sequencing logic runs against this at Tier 1.
public sealed class FakeRconChannelFactory(
    string password, IReadOnlyDictionary<string, string[]>? responses = null) : IRconChannelFactory
{
    public FakeRconChannel? Last { get; private set; }

    public Task<IRconChannel> OpenAsync(RconEndpoint endpoint, CancellationToken ct)
    {
        Last = new FakeRconChannel(password, responses ?? new Dictionary<string, string[]>());
        return Task.FromResult<IRconChannel>(Last);
    }
}

public sealed class FakeRconChannel(string password, IReadOnlyDictionary<string, string[]> responses) : IRconChannel
{
    private readonly Queue<RconPacket> _incoming = new();

    public List<RconPacket> Sent { get; } = [];

    public Task SendAsync(RconPacket packet, CancellationToken ct)
    {
        Sent.Add(packet);
        switch (packet.Type)
        {
            case RconPacketTypes.Auth:
                _incoming.Enqueue(new RconPacket(packet.Id, RconPacketTypes.ResponseValue, string.Empty)); // pre-auth junk
                bool accepted = packet.Body == password;
                _incoming.Enqueue(new RconPacket(
                    accepted ? packet.Id : RconPacketTypes.AuthFailureId, RconPacketTypes.AuthResponse, string.Empty));
                break;

            case RconPacketTypes.ExecCommand:
                string[] parts = responses.TryGetValue(packet.Body, out string[]? mapped) ? mapped : [string.Empty];
                foreach (string part in parts)
                {
                    _incoming.Enqueue(new RconPacket(packet.Id, RconPacketTypes.ResponseValue, part));
                }

                break;

            case RconPacketTypes.ResponseValue: // the client's sentinel follow-up
                _incoming.Enqueue(new RconPacket(packet.Id, RconPacketTypes.ResponseValue, string.Empty));
                break;
        }

        return Task.CompletedTask;
    }

    public Task<RconPacket> ReceiveAsync(CancellationToken ct) => Task.FromResult(_incoming.Dequeue());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
