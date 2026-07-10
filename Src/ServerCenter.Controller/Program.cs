using Microsoft.AspNetCore.Server.Kestrel.Core;
using ServerCenter.Controller.Grpc;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Connection;
using ServerCenter.Core.Identity;
using ServerCenter.Core.Jobs;

// The controller: source of truth and root of trust. Hosts the AgentLink gRPC endpoint agents
// dial into. Precious state (jobs, identities) persists in SQLite; live presence stays in
// memory. libvirt, S3, and mTLS identity land across later ships.
var builder = WebApplication.CreateBuilder(args);

// Plaintext HTTP/2 (h2c) for dev until TLS/mTLS lands (brief 3.8). WebApplicationFactory
// overrides this with its in-memory TestServer, so integration tests are unaffected.
builder.WebHost.ConfigureKestrel(options =>
    options.ListenLocalhost(5080, listen => listen.Protocols = HttpProtocols.Http2));

var databasePath = builder.Configuration["Database:Path"] ?? "servercenter.db";

builder.Services.AddGrpc();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new ServerCenterDatabase(databasePath));
builder.Services.AddSingleton<JobRepository>();
builder.Services.AddSingleton<AgentNodeRepository>();
builder.Services.AddSingleton<TrustRepository>();

// Precious state: SQLite-backed. Live presence: in-memory (transient, not precious).
builder.Services.AddSingleton<IControllerJobView, SqliteControllerJobView>();
builder.Services.AddSingleton<AgentPresenceStore>();
builder.Services.AddSingleton<IControllerSessionSink>(sp => sp.GetRequiredService<AgentPresenceStore>());

// Controller-owned identity (private CA). The mTLS transport enforcement that presents/pins
// these certs on the wire is the next ship; the dial stays plaintext h2c until then.
builder.Services.AddSingleton<ControllerOwnedTrustProvider>();
builder.Services.AddSingleton<IAgentTrustProvider>(sp => sp.GetRequiredService<ControllerOwnedTrustProvider>());

var app = builder.Build();

// Apply the schema (WAL + migrations) and ensure the CA exists before serving.
await app.Services.GetRequiredService<ServerCenterDatabase>().InitializeAsync(CancellationToken.None);
await app.Services.GetRequiredService<ControllerOwnedTrustProvider>().EnsureCaAsync(CancellationToken.None);

app.MapGrpcService<AgentLinkService>();
app.MapGet("/", () => "ServerCenter Controller. AgentLink gRPC endpoint is mapped.");

app.Run();

// Exposed as a public type so the integration test project can host it via
// WebApplicationFactory<Program>.
public partial class Program;
