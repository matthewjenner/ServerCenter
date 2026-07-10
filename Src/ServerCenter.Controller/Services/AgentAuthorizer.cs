using System.Security.Cryptography.X509Certificates;
using ServerCenter.Controller.Crypto;
using ServerCenter.Core.Identity;

namespace ServerCenter.Controller.Services;

// The connect-time authorization decision (the enforcement half of mTLS). A presented client
// cert authenticates the claimed agent id only if the cert's CN equals that id AND its pinned
// fingerprint verifies against the trust store. Binding CN to the claimed id stops one valid
// agent from impersonating another. On success, flips the identity pending -> active.
public sealed class AgentAuthorizer(ControllerOwnedTrustProvider trust)
{
    public async Task<bool> AuthorizeAsync(X509Certificate2? clientCertificate, string claimedAgentId, CancellationToken ct)
    {
        if (clientCertificate is null)
        {
            return false;
        }

        string subjectCommonName = clientCertificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        if (!string.Equals(subjectCommonName, claimedAgentId, StringComparison.Ordinal))
        {
            return false;
        }

        string fingerprint = CertificateAuthority.Fingerprint(clientCertificate);
        if (!await trust.VerifyAsync(new PresentedIdentity(claimedAgentId, fingerprint), ct))
        {
            return false;
        }

        await trust.MarkActiveAsync(claimedAgentId, ct);
        return true;
    }
}
