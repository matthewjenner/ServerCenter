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
        using RSA rsa = RSA.Create(3072);
        CertificateRequest request = new CertificateRequest(
            "CN=ServerCenter Controller CA", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        using X509Certificate2 ca = request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(10));
        return new CaMaterial(ca.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem());
    }

    public static IssuedCert IssueClientCert(CaMaterial ca, string subjectName, DateTimeOffset now)
    {
        using X509Certificate2 caCert = X509Certificate2.CreateFromPem(ca.CertPem, ca.KeyPem);
        using RSA rsa = RSA.Create(2048);
        CertificateRequest request = new CertificateRequest(
            $"CN={subjectName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid(ClientAuthOid) }, false));

        byte[] serial = RandomNumberGenerator.GetBytes(16);
        using X509Certificate2 issued = request.Create(caCert, now.AddMinutes(-5), now.AddYears(1), serial);
        return new IssuedCert(issued.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem(), Fingerprint(issued));
    }

    // The controller's own TLS server cert, signed by the CA. Agents validate the controller by
    // chaining this to the CA they received at enrollment. Regenerated at startup is fine (any
    // cert the CA signs is trusted), so it need not be persisted.
    public static IssuedCert IssueServerCert(CaMaterial ca, string dnsName, DateTimeOffset now)
    {
        using X509Certificate2 caCert = X509Certificate2.CreateFromPem(ca.CertPem, ca.KeyPem);
        using RSA rsa = RSA.Create(2048);
        CertificateRequest request = new CertificateRequest(
            $"CN={dnsName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false)); // server auth

        SubjectAlternativeNameBuilder san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(dnsName);
        san.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());

        byte[] serial = RandomNumberGenerator.GetBytes(16);
        using X509Certificate2 issued = request.Create(caCert, now.AddMinutes(-5), now.AddYears(1), serial);
        return new IssuedCert(issued.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem(), Fingerprint(issued));
    }

    // A cert loaded from PEM has an ephemeral key SChannel/Kestrel cannot use directly on
    // Windows; round-trip through PKCS#12 to get a usable private key.
    public static X509Certificate2 ToUsableCertificate(string certPem, string keyPem)
    {
        using X509Certificate2 fromPem = X509Certificate2.CreateFromPem(certPem, keyPem);
        byte[] pkcs12 = fromPem.Export(X509ContentType.Pkcs12);
        return X509CertificateLoader.LoadPkcs12(pkcs12, null);
    }

    public static string Fingerprint(X509Certificate2 cert) => Convert.ToHexString(SHA256.HashData(cert.RawData));

    public static string FingerprintFromPem(string certPem)
    {
        using X509Certificate2 cert = X509Certificate2.CreateFromPem(certPem);
        return Fingerprint(cert);
    }
}

public sealed record CaMaterial(string CertPem, string KeyPem);

public sealed record IssuedCert(string CertPem, string PrivateKeyPem, string Fingerprint);
