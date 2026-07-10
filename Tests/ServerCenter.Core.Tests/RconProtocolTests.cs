using System.Buffers.Binary;
using AwesomeAssertions;
using ServerCenter.Core.Primitives;
using ServerCenter.Primitives.Rcon;
using Xunit;

namespace ServerCenter.Core.Tests;

// The Source RCON wire framing: little-endian length prefix, id/type, null-terminated body.
public sealed class RconProtocolTests
{
    [Fact]
    public void Encode_then_decode_round_trips()
    {
        var packet = new RconPacket(7, RconPacketTypes.ExecCommand, "status");

        var bytes = RconProtocol.Encode(packet);
        var length = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        var content = bytes.AsSpan(4).ToArray();

        content.Length.Should().Be(length);
        RconProtocol.Decode(content).Should().Be(packet);
    }

    [Fact]
    public void Encode_writes_little_endian_length_and_trailing_nulls()
    {
        var bytes = RconProtocol.Encode(new RconPacket(1, RconPacketTypes.Auth, "pw"));

        // length = id(4) + type(4) + "pw"(2) + two nulls = 12
        BinaryPrimitives.ReadInt32LittleEndian(bytes).Should().Be(12);
        bytes[^1].Should().Be(0);
        bytes[^2].Should().Be(0);
    }

    [Fact]
    public void Decode_rejects_a_truncated_packet()
    {
        var act = () => RconProtocol.Decode(new byte[] { 1, 0, 0 });

        act.Should().Throw<InvalidDataException>();
    }
}
