using AwesomeAssertions;
using ServerCenter.Core.Connection;
using ServerCenter.Core.Transport;
using Xunit;

namespace ServerCenter.Core.Tests;

public sealed class HandshakeNegotiatorTests
{
    [Fact]
    public void Accepts_matching_major_and_negotiates_minor_down()
    {
        var decision = HandshakeNegotiator.Decide(
            peerMajor: ProtocolVersion.Major,
            peerMinor: ProtocolVersion.Minor + 3,
            sessionIdFactory: () => "sess");

        decision.Accepted.Should().BeTrue();
        decision.SessionId.Should().Be("sess");
        decision.NegotiatedMinor.Should().Be(ProtocolVersion.Minor); // lower of the two
    }

    [Fact]
    public void Rejects_mismatched_major()
    {
        var decision = HandshakeNegotiator.Decide(
            peerMajor: ProtocolVersion.Major + 1,
            peerMinor: 0,
            sessionIdFactory: () => "sess");

        decision.Accepted.Should().BeFalse();
        decision.RejectReason.Should().NotBeEmpty();
    }
}
