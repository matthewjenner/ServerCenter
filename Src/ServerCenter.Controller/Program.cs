using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using ServerCenter.Controller;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;

// The controller: source of truth and root of trust. Hosts the AgentLink gRPC endpoint agents
// dial into, plus the token-gated enrollment endpoint.
var builder = WebApplication.CreateBuilder(args);

var databasePath = builder.Configuration["Database:Path"] ?? "servercenter.db";
var requireClientCertificate = builder.Configuration.GetValue("Security:RequireClientCertificate", true);
var templatesRoot = builder.Configuration["Templates:Root"] ?? "templates";

// The CA must exist before Kestrel binds so we can mint the server TLS cert. Initialize the DB
// and CA up front (idempotent), reusing the same database instance in DI.
var database = new ServerCenterDatabase(databasePath);
await database.InitializeAsync(CancellationToken.None);
var bootstrapTrust = new ControllerOwnedTrustProvider(new TrustRepository(database), TimeProvider.System);
await bootstrapTrust.EnsureCaAsync(CancellationToken.None);

if (requireClientCertificate)
{
    // mTLS: HTTPS with a CA-signed server cert; accept client certs (per-connection authorization
    // is enforced in AgentLinkService, and the enrollment endpoint allows none).
    var serverCertificate = await bootstrapTrust.CreateServerCertificateAsync("localhost", CancellationToken.None);
    builder.WebHost.ConfigureKestrel(options =>
        options.ListenLocalhost(5443, listen => listen.UseHttps(serverCertificate, https =>
        {
            https.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
            https.ClientCertificateValidation = (_, _, _) => true;
        })));
}
else
{
    // Dev / tests: plaintext HTTP/2 (h2c), no client certs.
    builder.WebHost.ConfigureKestrel(options =>
        options.ListenLocalhost(5080, listen => listen.Protocols = HttpProtocols.Http2));
}

builder.Services.AddControllerServices(database, requireClientCertificate, templatesRoot);

var app = builder.Build();

// Idempotent no-op after the bootstrap above (the DI instance is the same database).
await app.Services.GetRequiredService<ServerCenterDatabase>().InitializeAsync(CancellationToken.None);
await app.Services.GetRequiredService<ControllerOwnedTrustProvider>().EnsureCaAsync(CancellationToken.None);

app.MapControllerEndpoints();

app.Run();

// Exposed as a public type so the integration test project can host it via
// WebApplicationFactory<Program>.
public partial class Program;
