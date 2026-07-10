using AwesomeAssertions;
using ServerCenter.Core.Transport;
using Xunit;

namespace ServerCenter.Core.Tests;

public sealed class ProtocolVersionTests
{
    [Fact]
    public void IsCompatible_true_for_matching_major() =>
        ProtocolVersion.IsCompatible(ProtocolVersion.Major).Should().BeTrue();

    [Fact]
    public void IsCompatible_false_for_mismatched_major() =>
        ProtocolVersion.IsCompatible(ProtocolVersion.Major + 1).Should().BeFalse();

    [Fact]
    public void NegotiateMinor_picks_the_lower_of_the_two() =>
        ProtocolVersion.NegotiateMinor(ProtocolVersion.Minor + 5).Should().Be(ProtocolVersion.Minor);
}
