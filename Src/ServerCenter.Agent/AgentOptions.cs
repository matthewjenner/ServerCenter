namespace ServerCenter.Agent;

// Agent configuration, read from environment variables (set by the systemd unit's
// EnvironmentFile on a real node, or the shell in dev). The SAME agent binary runs on the host
// and on every guest; only these values differ per node (notably NodeKind = host for node zero).
public sealed record AgentOptions(
    string ControllerAddress,
    string CertDirectory,
    string DisplayName,
    string? EnrollToken,
    string NodeKind,
    string? DevAgentId,
    TimeSpan HeartbeatInterval)
{
    public static AgentOptions FromEnvironment() => new(
        ControllerAddress: Environment.GetEnvironmentVariable("SERVERCENTER_CONTROLLER") ?? "https://localhost:5443",
        CertDirectory: Environment.GetEnvironmentVariable("SERVERCENTER_CERT_DIR") ?? "agent-identity",
        DisplayName: Environment.GetEnvironmentVariable("SERVERCENTER_AGENT_NAME") ?? Environment.MachineName,
        EnrollToken: Environment.GetEnvironmentVariable("SERVERCENTER_ENROLL_TOKEN"),
        NodeKind: Environment.GetEnvironmentVariable("SERVERCENTER_NODE_KIND") ?? "guest",
        DevAgentId: Environment.GetEnvironmentVariable("SERVERCENTER_AGENT_ID"),
        HeartbeatInterval: TimeSpan.FromSeconds(10));
}
