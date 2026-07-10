using Grpc.Core;
using ServerCenter.Contracts.V1;

namespace ServerCenter.Controller.Grpc;

// The server side of the bidi stream. Phase 1 implements: Hello/HelloAck handshake with mTLS
// identity pinning, heartbeat/status ingestion, the resync handshake, and command push. For
// now it is a mapped-but-unimplemented endpoint so the wire and hosting are proven.
public sealed class AgentLinkService : AgentLink.AgentLinkBase
{
    public override Task Connect(
        IAsyncStreamReader<AgentMessage> requestStream,
        IServerStreamWriter<ControllerMessage> responseStream,
        ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.Unimplemented,
            "AgentLink.Connect lands in Phase 1 (handshake, heartbeat, resync, command push)."));
}
