using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace ServerCenter.Agent;

// The agent's bootstrap bundle received from the controller's /enroll endpoint.
public sealed record EnrollmentBundle(
    string AgentId, string CertPem, string PrivateKeyPem, string CaCertPem, string Fingerprint);

// Fetches the agent's identity from the controller at bootstrap, gated by the one-time token.
// The token is delivered out-of-band (cloud-init / the last SSH). Server-cert trust at
// enrollment is trust-on-first-use here; the returned CA cert bootstraps trust for the mTLS
// stream afterward. In production the CA fingerprint would also be pinned out-of-band.
public static class EnrollmentClient
{
    public static async Task<EnrollmentBundle> EnrollAsync(
        string httpsBaseAddress, string displayName, string token, CancellationToken ct)
    {
        using var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = (_, _, _, _) => true }
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(httpsBaseAddress) };

        var response = await http.PostAsJsonAsync("/enroll", new EnrollBody(displayName, token), ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<EnrollResult>(ct)
            ?? throw new InvalidOperationException("empty enrollment response");
        return new EnrollmentBundle(result.AgentId, result.CertPem, result.PrivateKeyPem, result.CaCertPem, result.Fingerprint);
    }

    private sealed record EnrollBody(string DisplayName, string Token);

    private sealed record EnrollResult(
        string AgentId, string CertPem, string PrivateKeyPem, string CaCertPem, string Fingerprint);
}

public static class AgentTls
{
    // Builds mTLS material from an enrollment bundle: a client cert with a usable private key
    // (PKCS#12 round-trip) plus the CA cert to validate the controller.
    public static AgentTlsMaterial ToTlsMaterial(EnrollmentBundle bundle)
    {
        using var fromPem = X509Certificate2.CreateFromPem(bundle.CertPem, bundle.PrivateKeyPem);
        var clientCertificate = X509CertificateLoader.LoadPkcs12(fromPem.Export(X509ContentType.Pkcs12), null);
        var caCertificate = X509Certificate2.CreateFromPem(bundle.CaCertPem);
        return new AgentTlsMaterial(clientCertificate, caCertificate);
    }
}
