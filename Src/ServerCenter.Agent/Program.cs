using System.Runtime.InteropServices;
using ServerCenter.Agent;
using ServerCenter.Core.Connection;

// The agent host. Dials the controller and runs the outbound connection loop (handshake,
// heartbeat/status, resync, reconnect-with-backoff). Per-OS actions come from
// ServerCenter.Agent.Linux / .Windows behind the Core platform interfaces (wired in later
// phases).
//
// Transport: if the controller address is https, the agent runs mTLS - it enrolls once (using
// SERVERCENTER_ENROLL_TOKEN) to obtain its controller-minted cert, persists it, and presents it
// on every connect. If the address is http, it runs the plaintext dev path.

var controllerAddress = Environment.GetEnvironmentVariable("SERVERCENTER_CONTROLLER") ?? "https://localhost:5443";
var certDirectory = Environment.GetEnvironmentVariable("SERVERCENTER_CERT_DIR") ?? "agent-identity";
var displayName = Environment.GetEnvironmentVariable("SERVERCENTER_AGENT_NAME") ?? Environment.MachineName;

var osFamily = OperatingSystem.IsWindows() ? "windows" : "linux";
var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

GrpcTransportConnector connector;
AgentIdentity identity;

if (controllerAddress.StartsWith("https", StringComparison.OrdinalIgnoreCase))
{
    var store = new AgentCertStore(certDirectory);
    if (!store.Exists)
    {
        var token = Environment.GetEnvironmentVariable("SERVERCENTER_ENROLL_TOKEN");
        if (string.IsNullOrEmpty(token))
        {
            Console.Error.WriteLine(
                "No local identity and no SERVERCENTER_ENROLL_TOKEN set; cannot bootstrap mTLS. " +
                "Obtain a one-time enrollment token from the controller and set it, or point " +
                "SERVERCENTER_CONTROLLER at an http:// address for plaintext dev.");
            return 1;
        }

        Console.WriteLine($"Enrolling '{displayName}' with {controllerAddress} ...");
        var bundle = await EnrollmentClient.EnrollAsync(controllerAddress, displayName, token, cts.Token);
        store.Save(bundle);
        Console.WriteLine($"Enrolled as agent {bundle.AgentId}.");
    }

    identity = new AgentIdentity(store.AgentId, "0.1.0", osFamily, arch);
    connector = new GrpcTransportConnector(controllerAddress, store.LoadTls());
}
else
{
    // Plaintext dev: allow HTTP/2 over cleartext and use a fixed dev id (no enrollment).
    AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    var agentId = Environment.GetEnvironmentVariable("SERVERCENTER_AGENT_ID") ?? "dev-agent";
    identity = new AgentIdentity(agentId, "0.1.0", osFamily, arch);
    connector = new GrpcTransportConnector(controllerAddress);
}

var options = new AgentConnectionOptions(
    HeartbeatInterval: TimeSpan.FromSeconds(10),
    Backoff: new BackoffPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30)));

Console.WriteLine($"ServerCenter Agent '{identity.AgentId}' dialing {controllerAddress} (Ctrl+C to stop).");

await AgentConnection.RunAsync(
    connector.ConnectAsync,
    identity,
    new EmptyAgentJobStateSource(),
    new BasicAgentStatusSource(),
    new NoopCommandHandler(),
    TimeProvider.System,
    options,
    cts.Token);

Console.WriteLine("ServerCenter Agent stopped.");
return 0;
