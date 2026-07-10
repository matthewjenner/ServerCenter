using System.Runtime.InteropServices;
using ServerCenter.Agent;
using ServerCenter.Core.Connection;

// The agent host. Dials the controller and runs the outbound connection loop (handshake,
// heartbeat/status, resync, reconnect-with-backoff). Per-OS actions come from
// ServerCenter.Agent.Linux / .Windows behind the Core platform interfaces (wired in later
// phases). mTLS identity replaces the plaintext dial + placeholder id in a later Phase 1 ship.

var controllerAddress = Environment.GetEnvironmentVariable("SERVERCENTER_CONTROLLER") ?? "http://localhost:5080";
var agentId = Environment.GetEnvironmentVariable("SERVERCENTER_AGENT_ID") ?? "dev-agent";

// Allow HTTP/2 over cleartext for the dev plaintext dial until TLS lands.
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var identity = new AgentIdentity(
    agentId,
    "0.1.0",
    OperatingSystem.IsWindows() ? "windows" : "linux",
    RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());

var connector = new GrpcTransportConnector(controllerAddress);
var options = new AgentConnectionOptions(
    HeartbeatInterval: TimeSpan.FromSeconds(10),
    Backoff: new BackoffPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30)));

Console.WriteLine($"ServerCenter Agent '{agentId}' dialing {controllerAddress} (Ctrl+C to stop).");

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
