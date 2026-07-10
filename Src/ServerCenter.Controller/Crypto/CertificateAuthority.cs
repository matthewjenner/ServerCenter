using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ServerCenter.Controller.Crypto;

// The controller's private CA (brief 3.8): mints and signs per-agent client certs. Pure crypto,
// no persistence, so it is directly testable. Keys are RSA; the fingerprint is sha256 over the
// cert DER, which is what the controller pins.
public static class CertificateAuthority
{
    private const string ClientAuthOid = "1.3.6.1.5.5.7.3.2";

    public static CaMaterial CreateCa(DateTimeOffset now)
    {
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest(
            "CN=ServerCenter Controller CA", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        using var ca = request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(10));
        return new CaMaterial(ca.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem());
    }

    public static IssuedCert IssueClientCert(CaMaterial ca, string subjectName, DateTimeOffset now)
    {
        using var caCert = X509Certificate2.CreateFromPem(ca.CertPem, ca.KeyPem);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={subjectName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid(ClientAuthOid) }, false));

        var serial = RandomNumberGenerator.GetBytes(16);
        using var issued = request.Create(caCert, now.AddMinutes(-5), now.AddYears(1), serial);
        return new IssuedCert(issued.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem(), Fingerprint(issued));
    }

    public static string Fingerprint(X509Certificate2 cert) => Convert.ToHexString(SHA256.HashData(cert.RawData));

    public static string FingerprintFromPem(string certPem)
    {
        using var cert = X509Certificate2.CreateFromPem(certPem);
        return Fingerprint(cert);
    }
}

public sealed record CaMaterial(string CertPem, string KeyPem);

public sealed record IssuedCert(string CertPem, string PrivateKeyPem, string Fingerprint);
