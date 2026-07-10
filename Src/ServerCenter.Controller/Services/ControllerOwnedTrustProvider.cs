using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ServerCenter.Controller.Crypto;
using ServerCenter.Controller.Persistence;
using ServerCenter.Core.Identity;

namespace ServerCenter.Controller.Services;

// The controller-owned trust root (brief 3.8): mints, stores, and pins per-agent identity.
// Behind IAgentTrustProvider so a future agent-held-key provider could replace it without
// touching callers. Enrollment is gated by a one-time bootstrap token; verification pins the
// sha256 fingerprint; rotation re-pins; revocation stops verification.
public sealed class ControllerOwnedTrustProvider(TrustRepository trust, TimeProvider clock) : IAgentTrustProvider
{
    // Ensures the CA exists (called once at controller startup). Idempotent under races.
    public async Task EnsureCaAsync(CancellationToken ct)
    {
        if (await trust.GetCaAsync(ct) is not null)
        {
            return;
        }

        DateTimeOffset now = clock.GetUtcNow();
        await trust.SaveCaAsync(CertificateAuthority.CreateCa(now), now.ToUnixTimeMilliseconds(), ct);
    }

    // Operator action: issue a one-time, short-TTL bootstrap token to seed a new agent. Only the
    // hash is stored; the returned plaintext is delivered out-of-band (cloud-init / the last SSH).
    public async Task<string> CreateBootstrapTokenAsync(string displayName, TimeSpan ttl, CancellationToken ct)
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        long expiresAt = clock.GetUtcNow().Add(ttl).ToUnixTimeMilliseconds();
        await trust.InsertTokenAsync(Sha256Hex(token), displayName, expiresAt, ct);
        return token;
    }

    public async Task<EnrollmentResult> EnrollAsync(EnrollmentRequest request, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();
        string? consumed = await trust.ConsumeTokenAsync(Sha256Hex(request.OneTimeToken), now.ToUnixTimeMilliseconds(), ct);
        if (consumed is null)
        {
            throw new InvalidOperationException("Bootstrap token is invalid, already used, or expired.");
        }

        CaMaterial ca = await RequireCaAsync(ct);
        string agentId = Guid.NewGuid().ToString("N"); // controller mints the identity id
        IssuedCert issued = CertificateAuthority.IssueClientCert(ca, agentId, now);

        // Pending until the agent's first successful connect flips it to active.
        await trust.InsertIdentityAsync(
            agentId, request.DisplayName, issued.Fingerprint, "pending", now.ToUnixTimeMilliseconds(), ct);

        return new EnrollmentResult(agentId, issued.CertPem, issued.PrivateKeyPem, ca.CertPem, issued.Fingerprint);
    }

    public async Task<bool> VerifyAsync(PresentedIdentity presented, CancellationToken ct)
    {
        AgentIdentityRow? identity = await trust.GetIdentityAsync(presented.AgentId, ct);
        return identity is not null
            && identity.Status is "pending" or "active"
            && string.Equals(identity.CertFpr, presented.CertFingerprint, StringComparison.OrdinalIgnoreCase);
    }

    // Flip pending -> active on the agent's first successful connect.
    public async Task MarkActiveAsync(string agentId, CancellationToken ct) =>
        await trust.SetStatusAsync(agentId, "active", null, ct);

    public async Task<EnrollmentResult> RotateAsync(string agentId, CancellationToken ct)
    {
        AgentIdentityRow identity = await trust.GetIdentityAsync(agentId, ct)
            ?? throw new InvalidOperationException($"Unknown agent '{agentId}'.");

        DateTimeOffset now = clock.GetUtcNow();
        CaMaterial ca = await RequireCaAsync(ct);
        IssuedCert issued = CertificateAuthority.IssueClientCert(ca, agentId, now);
        await trust.SetFingerprintAsync(agentId, issued.Fingerprint, now.ToUnixTimeMilliseconds(), ct);

        return new EnrollmentResult(agentId, issued.CertPem, issued.PrivateKeyPem, ca.CertPem, issued.Fingerprint);
    }

    public async Task RevokeAsync(string agentId, CancellationToken ct) =>
        await trust.SetStatusAsync(agentId, "revoked", clock.GetUtcNow().ToUnixTimeMilliseconds(), ct);

    // The controller's own TLS server cert, signed by the CA, ready for Kestrel. Regenerated at
    // startup; agents trust it by chaining to the CA they hold.
    public async Task<X509Certificate2> CreateServerCertificateAsync(string dnsName, CancellationToken ct)
    {
        CaMaterial ca = await RequireCaAsync(ct);
        IssuedCert issued = CertificateAuthority.IssueServerCert(ca, dnsName, clock.GetUtcNow());
        return CertificateAuthority.ToUsableCertificate(issued.CertPem, issued.PrivateKeyPem);
    }

    private async Task<CaMaterial> RequireCaAsync(CancellationToken ct) =>
        await trust.GetCaAsync(ct) ?? throw new InvalidOperationException("Controller CA is not initialized.");

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
