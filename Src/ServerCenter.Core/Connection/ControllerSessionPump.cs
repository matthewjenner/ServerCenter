using ServerCenter.Contracts.V1;
using ServerCenter.Core.Transport;

namespace ServerCenter.Core.Connection;

// The controller's steady-state loop for one connected agent after a successful handshake:
// read what the agent pushes up and deliver it to the sink. Returns when the stream ends
// (which is the signal the agent went offline). Command push down is added in Phase 3.
public static class ControllerSessionPump
{
    public static async Task RunAsync(
        IControllerStream stream,
        string agentId,
        IControllerSessionSink sink,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(sink);

        await foreach (AgentMessage msg in stream.Incoming(ct))
        {
            switch (msg.PayloadCase)
            {
                case AgentMessage.PayloadOneofCase.Heartbeat:
                    await sink.OnHeartbeatAsync(agentId, msg.Heartbeat, ct);
                    break;
                case AgentMessage.PayloadOneofCase.Status:
                    await sink.OnStatusAsync(agentId, msg.Status, ct);
                    break;
                case AgentMessage.PayloadOneofCase.JobProgress:
                    await sink.OnJobProgressAsync(agentId, msg.JobProgress, ct);
                    break;
                case AgentMessage.PayloadOneofCase.CommandResult:
                    await sink.OnCommandResultAsync(agentId, msg.CommandResult, ct);
                    break;
                default:
                    // Hello / JobResync are consumed by the handshake.
                    break;
            }
        }
    }
}
