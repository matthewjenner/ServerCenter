// The agent host. Cross-platform; per-OS actions come from ServerCenter.Agent.Linux /
// ServerCenter.Agent.Windows behind the Core platform interfaces.
//
// Scaffold only. Phase 1 builds the real connection loop here: dial the controller, open the
// bidi stream through an IAgentTransport (GrpcAgentTransport), Hello/HelloAck handshake,
// heartbeat, status, and the resync handshake before any real job exists.
Console.WriteLine("ServerCenter Agent (scaffold). Connection loop lands in Phase 1.");
