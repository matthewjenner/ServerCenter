namespace ServerCenter.Core.Transport;

// The envelope version rules (phase-0-contracts.md 1.1) as pure, testable logic. Major
// mismatch = reject the stream (Goodbye VERSION_MISMATCH). Minor is additive and negotiated
// down to the lower of the two peers.
public static class ProtocolVersion
{
    public const uint Major = 1;
    public const uint Minor = 0;

    // Major must match exactly; the receiver must not interpret payloads on mismatch.
    public static bool IsCompatible(uint peerMajor) => peerMajor == Major;

    // Both peers operate at the lower minor so neither sends a field the other lacks.
    public static uint NegotiateMinor(uint peerMinor) => Math.Min(Minor, peerMinor);
}
