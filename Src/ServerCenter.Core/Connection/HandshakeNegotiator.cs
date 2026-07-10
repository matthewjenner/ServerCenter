using ServerCenter.Core.Transport;

namespace ServerCenter.Core.Connection;

// The pure version-negotiation decision for the Hello/HelloAck handshake (phase-0-contracts.md
// 1.1). Major mismatch = reject (Goodbye VERSION_MISMATCH), no payload interpretation. Minor
// is negotiated down to the lower of the two peers.
public static class HandshakeNegotiator
{
    public static HandshakeDecision Decide(uint peerMajor, uint peerMinor, Func<string> sessionIdFactory)
    {
        ArgumentNullException.ThrowIfNull(sessionIdFactory);

        if (!ProtocolVersion.IsCompatible(peerMajor))
        {
            return new HandshakeDecision
            {
                Accepted = false,
                RejectReason = $"protocol major {peerMajor} incompatible with {ProtocolVersion.Major}"
            };
        }

        return new HandshakeDecision
        {
            Accepted = true,
            NegotiatedMinor = ProtocolVersion.NegotiateMinor(peerMinor),
            SessionId = sessionIdFactory()
        };
    }
}

public sealed record HandshakeDecision
{
    public required bool Accepted { get; init; }
    public uint NegotiatedMinor { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public string RejectReason { get; init; } = string.Empty;
}
