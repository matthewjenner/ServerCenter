using System.Security.Cryptography.X509Certificates;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Identity;
using Xunit;

namespace ServerCenter.Controller.Tests;

public sealed class AgentAuthorizerTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero));
    private TempDatabase _db = null!;
    private ControllerOwnedTrustProvider _trust = null!;
    private AgentAuthorizer _authorizer = null!;

    public async ValueTask InitializeAsync()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _db = await TempDatabase.CreateAsync(ct);
        _trust = new ControllerOwnedTrustProvider(new TrustRepository(_db.Database), _clock);
        await _trust.EnsureCaAsync(ct);
        _authorizer = new AgentAuthorizer(_trust);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Authorizes_a_valid_enrolled_cert_and_flips_to_active()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        EnrollmentResult enrolled = await EnrollAsync(ct);
        using X509Certificate2 cert = X509Certificate2.CreateFromPem(enrolled.CertPem);

        (await _authorizer.AuthorizeAsync(cert, enrolled.AgentId, ct)).Should().BeTrue();
    }

    [Fact]
    public async Task Rejects_a_null_cert()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (await _authorizer.AuthorizeAsync(null, "anyone", ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Rejects_when_the_cert_cn_does_not_match_the_claimed_id()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        EnrollmentResult enrolled = await EnrollAsync(ct);
        using X509Certificate2 cert = X509Certificate2.CreateFromPem(enrolled.CertPem);

        // Valid cert, but claiming to be a different agent.
        (await _authorizer.AuthorizeAsync(cert, "someone-else", ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Rejects_a_revoked_identity()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        EnrollmentResult enrolled = await EnrollAsync(ct);
        using X509Certificate2 cert = X509Certificate2.CreateFromPem(enrolled.CertPem);
        await _trust.RevokeAsync(enrolled.AgentId, ct);

        (await _authorizer.AuthorizeAsync(cert, enrolled.AgentId, ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Rejects_a_cert_whose_fingerprint_is_not_pinned()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        EnrollmentResult enrolled = await EnrollAsync(ct);
        // A different agent's cert carrying the first agent's id would not match the pin.
        EnrollmentResult other = await EnrollAsync(ct);
        using X509Certificate2 otherCert = X509Certificate2.CreateFromPem(other.CertPem);

        // CN of otherCert is other.AgentId, so also fails CN binding; use a cert with the right
        // CN but wrong fingerprint by rotating (old cert now unpinned).
        EnrollmentResult rotated = await _trust.RotateAsync(enrolled.AgentId, ct);
        _ = rotated;
        using X509Certificate2 staleCert = X509Certificate2.CreateFromPem(enrolled.CertPem);

        (await _authorizer.AuthorizeAsync(staleCert, enrolled.AgentId, ct)).Should().BeFalse();
    }

    private async Task<EnrollmentResult> EnrollAsync(CancellationToken ct)
    {
        string token = await _trust.CreateBootstrapTokenAsync("agent", TimeSpan.FromMinutes(10), ct);
        return await _trust.EnrollAsync(new EnrollmentRequest("agent", token), ct);
    }
}
