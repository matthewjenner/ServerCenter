using Microsoft.AspNetCore.Server.Kestrel.Core;
using ServerCenter.Controller.Grpc;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Connection;
using ServerCenter.Core.Jobs;

// The controller: source of truth and root of trust. Hosts the AgentLink gRPC endpoint agents
// dial into. Phase 1 wires the endpoint + in-memory presence/job-view; SQLite, libvirt, S3,
// and mTLS identity land across later ships.
var builder = WebApplication.CreateBuilder(args);

// Plaintext HTTP/2 (h2c) for dev until TLS/mTLS lands (brief 3.8). WebApplicationFactory
// overrides this with its in-memory TestServer, so integration tests are unaffected.
builder.WebHost.ConfigureKestrel(options =>
    options.ListenLocalhost(5080, listen => listen.Protocols = HttpProtocols.Http2));

builder.Services.AddGrpc();
builder.Services.AddSingleton<AgentPresenceStore>();
builder.Services.AddSingleton<IControllerSessionSink>(sp => sp.GetRequiredService<AgentPresenceStore>());
builder.Services.AddSingleton<IControllerJobView, InMemoryControllerJobView>();

var app = builder.Build();

app.MapGrpcService<AgentLinkService>();
app.MapGet("/", () => "ServerCenter Controller. AgentLink gRPC endpoint is mapped.");

app.Run();

// Exposed as a public type so the integration test project can host it via
// WebApplicationFactory<Program>.
public partial class Program;
