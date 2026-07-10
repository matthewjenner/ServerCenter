using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;

namespace ServerCenter.Controller;

// The controller: source of truth and root of trust. Hosts the AgentLink gRPC endpoint agents dial
// into, plus the token-gated enrollment endpoint. Public so the integration test project can host it
// via WebApplicationFactory<Program>.
public class Program
{
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        string databasePath = builder.Configuration["Database:Path"] ?? "servercenter.db";
        bool requireClientCertificate = builder.Configuration.GetValue("Security:RequireClientCertificate", true);
        string templatesRoot = builder.Configuration["Templates:Root"] ?? "templates";
        bool libvirtEnabled = builder.Configuration.GetValue("Libvirt:Enabled", false);
        string? libvirtConnectUri = builder.Configuration["Libvirt:ConnectUri"];

        // The CA must exist before Kestrel binds so we can mint the server TLS cert. Initialize the DB
        // and CA up front (idempotent), reusing the same database instance in DI.
        ServerCenterDatabase database = new(databasePath);
        await database.InitializeAsync(CancellationToken.None);
        ControllerOwnedTrustProvider bootstrapTrust = new(new TrustRepository(database), TimeProvider.System);
        await bootstrapTrust.EnsureCaAsync(CancellationToken.None);

        // Bind all interfaces (0.0.0.0), not loopback: the controller serves agents on other hosts/
        // guests (and in a container, loopback is unreachable from outside). The agent validates the
        // server by CA-chain, not hostname, so the cert subject stays "localhost".
        if (requireClientCertificate)
        {
            // mTLS: HTTPS with a CA-signed server cert; accept client certs (per-connection
            // authorization is enforced in AgentLinkService, and the enrollment endpoint allows none).
            X509Certificate2 serverCertificate =
                await bootstrapTrust.CreateServerCertificateAsync("localhost", CancellationToken.None);
            builder.WebHost.ConfigureKestrel(options =>
                options.ListenAnyIP(5443, listen => listen.UseHttps(serverCertificate, https =>
                {
                    https.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
                    https.ClientCertificateValidation = (_, _, _) => true;
                })));
        }
        else
        {
            // Dev / tests: plaintext HTTP/2 (h2c), no client certs.
            builder.WebHost.ConfigureKestrel(options =>
                options.ListenAnyIP(5080, listen => listen.Protocols = HttpProtocols.Http2));
        }

        builder.Services.AddControllerServices(
            database, requireClientCertificate, templatesRoot, libvirtEnabled, libvirtConnectUri);

        WebApplication app = builder.Build();

        // Idempotent no-op after the bootstrap above (the DI instance is the same database).
        await app.Services.GetRequiredService<ServerCenterDatabase>().InitializeAsync(CancellationToken.None);
        await app.Services.GetRequiredService<ControllerOwnedTrustProvider>().EnsureCaAsync(CancellationToken.None);

        app.MapControllerEndpoints();

        await app.RunAsync();
    }
}
