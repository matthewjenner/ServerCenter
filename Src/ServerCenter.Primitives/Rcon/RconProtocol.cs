using System.Buffers.Binary;
using System.Text;
using ServerCenter.Core.Primitives;

namespace ServerCenter.Primitives.Rcon;

// Source RCON wire framing (pure, directly unit-testable). A packet on the wire is:
//   int32 length (of everything after this field)  | int32 id | int32 type | body bytes | 0x00 0x00
// All integers little-endian; the body is null-terminated and the packet has a second null pad.
public static class RconProtocol
{
    // Source caps a response body at 4096 bytes; the framing adds id+type+two nulls. Guard reads so
    // a garbled/hostile length prefix cannot drive a huge allocation.
    public const int MaxContentBytes = 4096 + 10;

    public static byte[] Encode(RconPacket packet)
    {
        var body = Encoding.UTF8.GetBytes(packet.Body);
        var length = 4 + 4 + body.Length + 2; // id + type + body + two trailing nulls
        var buffer = new byte[4 + length];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, length);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), packet.Id);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(8), packet.Type);
        body.CopyTo(buffer.AsSpan(12));
        // The last two bytes remain 0 (body null terminator + packet pad).
        return buffer;
    }

    // Decodes the `length` bytes that follow the length prefix: id, type, body, 0x00, 0x00.
    public static RconPacket Decode(ReadOnlySpan<byte> content)
    {
        if (content.Length < 10)
        {
            throw new InvalidDataException($"RCON packet content too short ({content.Length} bytes)");
        }

        var id = BinaryPrimitives.ReadInt32LittleEndian(content);
        var type = BinaryPrimitives.ReadInt32LittleEndian(content[4..]);
        var body = Encoding.UTF8.GetString(content[8..^2]); // drop the two trailing nulls
        return new RconPacket(id, type, body);
    }
}
