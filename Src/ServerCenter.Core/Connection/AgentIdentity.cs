namespace ServerCenter.Core.Connection;

// What an agent presents in its Hello. The AgentId is the controller-owned, pinned identity
// (brief 3.8); mTLS enrollment that mints it lands in a later Phase 1 ship. For now it is
// supplied to the agent at construction.
public sealed record AgentIdentity(string AgentId, string AgentVersion, string OsFamily, string Arch);
