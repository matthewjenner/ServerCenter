namespace ServerCenter.Core.Identity;

// Root of trust is centralized; work is decentralized (phase-0-contracts.md section 8).
// Today: ControllerOwnedTrustProvider (controller mints and holds the CA, pins the per-agent
// cert fingerprint). The seam is left so a future AgentHeldKeyTrustProvider could verify
// agent-generated keys/CSRs without touching callers. Ships with an in-memory fake so the
// enroll/verify/rotate/revoke flows are Tier 1 testable without a real CA.
public interface IAgentTrustProvider
{
    Task<EnrollmentResult> EnrollAsync(EnrollmentRequest request, CancellationToken ct);

    Task<bool> VerifyAsync(PresentedIdentity presented, CancellationToken ct);

    // Mints a fresh cert for an existing agent, re-pins its fingerprint, and returns the new
    // bundle to deliver to the agent. The old fingerprint stops verifying.
    Task<EnrollmentResult> RotateAsync(string agentId, CancellationToken ct);

    Task RevokeAsync(string agentId, CancellationToken ct);
}

public sealed record EnrollmentRequest(string DisplayName, string OneTimeToken);

// The bundle handed to the agent at bootstrap: its issued client cert + private key, plus the
// CA cert so it can validate the controller. The fingerprint is what the controller pins.
public sealed record EnrollmentResult(
    string AgentId,
    string CertPem,
    string PrivateKeyPem,
    string CaCertPem,
    string CertFingerprint);

public sealed record PresentedIdentity(string AgentId, string CertFingerprint);
