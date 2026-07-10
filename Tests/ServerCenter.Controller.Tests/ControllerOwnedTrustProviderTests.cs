using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Identity;
using Xunit;

namespace ServerCenter.Controller.Tests;

public sealed class ControllerOwnedTrustProviderTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero));
    private TempDatabase _db = null!;
    private ControllerOwnedTrustProvider _trust = null!;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        _db = await TempDatabase.CreateAsync(ct);
        _trust = new ControllerOwnedTrustProvider(new TrustRepository(_db.Database), _clock);
        await _trust.EnsureCaAsync(ct);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Enroll_mints_a_bundle_and_pins_the_fingerprint()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await EnrollAsync(ct);

        result.AgentId.Should().NotBeEmpty();
        result.CertPem.Should().Contain("BEGIN CERTIFICATE");
        result.PrivateKeyPem.Should().Contain("PRIVATE KEY");
        result.CaCertPem.Should().Contain("BEGIN CERTIFICATE");

        (await _trust.VerifyAsync(new PresentedIdentity(result.AgentId, result.CertFingerprint), ct))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Verify_rejects_a_wrong_fingerprint()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await EnrollAsync(ct);

        (await _trust.VerifyAsync(new PresentedIdentity(result.AgentId, "DEADBEEF"), ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Verify_rejects_an_unknown_agent()
    {
        var ct = TestContext.Current.CancellationToken;
        (await _trust.VerifyAsync(new PresentedIdentity("nobody", "x"), ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Bootstrap_token_is_one_time()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = await _trust.CreateBootstrapTokenAsync("agent", TimeSpan.FromMinutes(10), ct);
        await _trust.EnrollAsync(new EnrollmentRequest("agent", token), ct);

        var reuse = async () => await _trust.EnrollAsync(new EnrollmentRequest("agent", token), ct);
        await reuse.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Expired_bootstrap_token_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = await _trust.CreateBootstrapTokenAsync("agent", TimeSpan.FromMinutes(1), ct);
        _clock.Advance(TimeSpan.FromMinutes(2));

        var enroll = async () => await _trust.EnrollAsync(new EnrollmentRequest("agent", token), ct);
        await enroll.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Revoke_stops_verification()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await EnrollAsync(ct);

        await _trust.RevokeAsync(result.AgentId, ct);

        (await _trust.VerifyAsync(new PresentedIdentity(result.AgentId, result.CertFingerprint), ct))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Rotate_repins_so_the_old_fingerprint_fails_and_the_new_one_verifies()
    {
        var ct = TestContext.Current.CancellationToken;
        var original = await EnrollAsync(ct);

        var rotated = await _trust.RotateAsync(original.AgentId, ct);

        rotated.CertFingerprint.Should().NotBe(original.CertFingerprint);
        (await _trust.VerifyAsync(new PresentedIdentity(original.AgentId, original.CertFingerprint), ct))
            .Should().BeFalse();
        (await _trust.VerifyAsync(new PresentedIdentity(original.AgentId, rotated.CertFingerprint), ct))
            .Should().BeTrue();
    }

    [Fact]
    public async Task MarkActive_keeps_verification_working()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await EnrollAsync(ct);

        await _trust.MarkActiveAsync(result.AgentId, ct);

        (await _trust.VerifyAsync(new PresentedIdentity(result.AgentId, result.CertFingerprint), ct))
            .Should().BeTrue();
    }

    private async Task<EnrollmentResult> EnrollAsync(CancellationToken ct)
    {
        var token = await _trust.CreateBootstrapTokenAsync("agent", TimeSpan.FromMinutes(10), ct);
        return await _trust.EnrollAsync(new EnrollmentRequest("agent", token), ct);
    }
}
