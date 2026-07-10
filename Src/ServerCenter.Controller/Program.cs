using ServerCenter.Controller.Grpc;

// The controller: source of truth and root of trust. Hosts the AgentLink gRPC endpoint that
// agents dial into. Scaffold wires the endpoint; the job engine, SQLite persistence, libvirt,
// S3, and mTLS identity land across Phases 1-6.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

var app = builder.Build();

app.MapGrpcService<AgentLinkService>();
app.MapGet("/", () => "ServerCenter Controller (scaffold). AgentLink gRPC endpoint is mapped.");

app.Run();
