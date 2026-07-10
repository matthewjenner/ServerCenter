using AwesomeAssertions;
using ServerCenter.Core.Connection;
using Xunit;

namespace ServerCenter.Core.Tests;

public sealed class BackoffPolicyTests
{
    [Fact]
    public void Full_jitter_at_max_rng_equals_the_exponential_cap()
    {
        var policy = new BackoffPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), () => 1.0);

        policy.NextDelay(0).Should().Be(TimeSpan.FromSeconds(1));
        policy.NextDelay(1).Should().Be(TimeSpan.FromSeconds(2));
        policy.NextDelay(2).Should().Be(TimeSpan.FromSeconds(4));
        policy.NextDelay(10).Should().Be(TimeSpan.FromSeconds(30)); // capped
    }

    [Fact]
    public void Full_jitter_at_zero_rng_is_zero() =>
        new BackoffPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), () => 0.0)
            .NextDelay(5).Should().Be(TimeSpan.Zero);

    [Fact]
    public void Delay_never_exceeds_max_even_for_huge_attempts() =>
        new BackoffPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), () => 1.0)
            .NextDelay(1000).Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(30));

    [Fact]
    public void Ctor_rejects_nonpositive_base() =>
        ((Action)(() => _ = new BackoffPolicy(TimeSpan.Zero, TimeSpan.FromSeconds(1))))
            .Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void Ctor_rejects_max_below_base() =>
        ((Action)(() => _ = new BackoffPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1))))
            .Should().Throw<ArgumentOutOfRangeException>();
}
