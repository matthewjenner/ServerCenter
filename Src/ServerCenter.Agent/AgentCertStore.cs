using System.Security.Cryptography.X509Certificates;

namespace ServerCenter.Agent;

// Persists the enrolled identity (cert + key + CA + agent id) to a local directory so the agent
// enrolls once and reuses its cert across restarts. This is the agent's only local state and is
// disposable (re-enroll rebuilds it). The private key file must be protected by the node's own
// filesystem permissions.
public sealed class AgentCertStore(string directory)
{
    private string CertFile => Path.Combine(directory, "agent-cert.pem");
    private string KeyFile => Path.Combine(directory, "agent-key.pem");
    private string CaFile => Path.Combine(directory, "ca-cert.pem");
    private string IdFile => Path.Combine(directory, "agent-id.txt");

    public bool Exists =>
        File.Exists(CertFile) && File.Exists(KeyFile) && File.Exists(CaFile) && File.Exists(IdFile);

    public string AgentId => File.ReadAllText(IdFile).Trim();

    public void Save(EnrollmentBundle bundle)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(CertFile, bundle.CertPem);
        File.WriteAllText(KeyFile, bundle.PrivateKeyPem);
        File.WriteAllText(CaFile, bundle.CaCertPem);
        File.WriteAllText(IdFile, bundle.AgentId);
    }

    public AgentTlsMaterial LoadTls()
    {
        using X509Certificate2 fromPem = X509Certificate2.CreateFromPem(File.ReadAllText(CertFile), File.ReadAllText(KeyFile));
        X509Certificate2 clientCertificate = X509CertificateLoader.LoadPkcs12(fromPem.Export(X509ContentType.Pkcs12), null);
        X509Certificate2 caCertificate = X509Certificate2.CreateFromPem(File.ReadAllText(CaFile));
        return new AgentTlsMaterial(clientCertificate, caCertificate);
    }
}
