namespace ServerCenter.Core.Connection;

// What an agent presents in its Hello. The AgentId is the controller-owned, pinned identity
// (brief 3.8). NodeKind is "guest" for normal managed nodes and "host" for node zero (the agent
// running natively on the hypervisor host, brief 3.4).
public sealed record AgentIdentity(
    string AgentId, string AgentVersion, string OsFamily, string Arch, string NodeKind = "guest");
