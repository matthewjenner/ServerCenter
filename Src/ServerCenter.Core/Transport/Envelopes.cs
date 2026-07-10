using ServerCenter.Contracts.V1;

namespace ServerCenter.Core.Transport;

// Stamps outgoing messages with the current protocol version and a unique message id. Kept
// in one place so every send is consistent (versioned envelope from message one).
public static class Envelopes
{
    public static Envelope New(string? correlationId = null) => new()
    {
        ProtocolMajor = ProtocolVersion.Major,
        ProtocolMinor = ProtocolVersion.Minor,
        MessageId = Guid.NewGuid().ToString("N"),
        CorrelationId = correlationId ?? string.Empty
    };
}
