namespace ServerCenter.Controller.Services;

// When true, the AgentLink stream requires a valid, pinned client certificate (mTLS enforced).
// When false, the controller runs plaintext h2c with the dev "unpinned" registration - used by
// the in-process integration test (TestServer has no TLS) and local dev before enrollment.
public sealed record AgentSecurityOptions(bool RequireClientCertificate);
