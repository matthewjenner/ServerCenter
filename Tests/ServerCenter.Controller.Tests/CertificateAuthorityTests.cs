using System.Security.Cryptography.X509Certificates;
using AwesomeAssertions;
using ServerCenter.Controller.Crypto;
using Xunit;

namespace ServerCenter.Controller.Tests;

public sealed class CertificateAuthorityTests
{
    [Fact]
    public void Issued_client_cert_chains_to_the_ca()
    {
        var now = DateTimeOffset.UtcNow;
        var ca = CertificateAuthority.CreateCa(now);
        var issued = CertificateAuthority.IssueClientCert(ca, "agent-1", now);

        using var caCert = X509Certificate2.CreateFromPem(ca.CertPem);
        using var clientCert = X509Certificate2.CreateFromPem(issued.CertPem);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(caCert);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        chain.Build(clientCert).Should().BeTrue();
    }

    [Fact]
    public void Fingerprint_round_trips_from_the_pem()
    {
        var now = DateTimeOffset.UtcNow;
        var ca = CertificateAuthority.CreateCa(now);
        var issued = CertificateAuthority.IssueClientCert(ca, "agent-1", now);

        CertificateAuthority.FingerprintFromPem(issued.CertPem).Should().Be(issued.Fingerprint);
    }

    [Fact]
    public void Distinct_issuances_have_distinct_fingerprints()
    {
        var now = DateTimeOffset.UtcNow;
        var ca = CertificateAuthority.CreateCa(now);

        var a = CertificateAuthority.IssueClientCert(ca, "agent-1", now);
        var b = CertificateAuthority.IssueClientCert(ca, "agent-2", now);

        a.Fingerprint.Should().NotBe(b.Fingerprint);
    }
}
