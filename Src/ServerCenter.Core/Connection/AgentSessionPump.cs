using ServerCenter.Contracts.V1;
using ServerCenter.Core.Transport;

namespace ServerCenter.Core.Connection;

// The agent's steady-state loop after a successful handshake: push heartbeat + status on a
// fixed cadence, and read controller messages, dispatching commands. Returns when the session
// ends (stream dropped, or the controller sent Goodbye). The reconnect decision is the
// caller's (AgentConnection). Time comes from an injected TimeProvider so the cadence is
// deterministic in tests.
public static class AgentSessionPump
{
    public static async Task<AgentSessionOutcome> RunAsync(
        IAgentTransport transport,
        IAgentStatusSource statusSource,
        IAgentCommandHandler commands,
        TimeProvider clock,
        TimeSpan heartbeatInterval,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(statusSource);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(clock);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var read = ReadLoopAsync(transport, commands, linked.Token);
        var beat = HeartbeatLoopAsync(transport, statusSource, clock, heartbeatInterval, linked.Token);

        await Task.WhenAny(read, beat);
        await linked.CancelAsync();

        // Whichever loop is still running was cancelled or will fail on the dropped stream;
        // either way the session is over. The read loop carries any Goodbye reason.
        GoodbyeReason? goodbye = null;
        try
        {
            goodbye = await read;
        }
        catch
        {
            // stream dropped or cancelled mid-read: treated as a plain session end
        }

        try
        {
            await beat;
        }
        catch
        {
            // heartbeat send failed on the dropped stream, or was cancelled
        }

        return goodbye is { } reason
            ? new AgentSessionOutcome(SessionEndKind.ControllerGoodbye, reason)
            : new AgentSessionOutcome(SessionEndKind.StreamEnded, GoodbyeReason.Unspecified);
    }

    private static async Task<GoodbyeReason?> ReadLoopAsync(
        IAgentTransport transport,
        IAgentCommandHandler commands,
        CancellationToken ct)
    {
        await foreach (var msg in transport.Incoming(ct))
        {
            switch (msg.PayloadCase)
            {
                case ControllerMessage.PayloadOneofCase.Command:
                    await commands.OnCommandAsync(msg.Command, ct);
                    break;
                case ControllerMessage.PayloadOneofCase.CancelJob:
                    await commands.OnCancelAsync(msg.CancelJob, ct);
                    break;
                case ControllerMessage.PayloadOneofCase.Goodbye:
                    return msg.Goodbye.Reason;
                default:
                    // HelloAck / ResyncRequest belong to the handshake, not steady state.
                    break;
            }
        }

        return null;
    }

    private static async Task HeartbeatLoopAsync(
        IAgentTransport transport,
        IAgentStatusSource statusSource,
        TimeProvider clock,
        TimeSpan interval,
        CancellationToken ct)
    {
        // Send one tick immediately so the controller has state without waiting a full interval.
        while (!ct.IsCancellationRequested)
        {
            await transport.SendAsync(
                new AgentMessage
                {
                    Envelope = Envelopes.New(),
                    Heartbeat = new Heartbeat { AgentUnixMs = clock.GetUtcNow().ToUnixTimeMilliseconds() }
                },
                ct);

            var status = await statusSource.GetStatusAsync(ct);
            await transport.SendAsync(
                new AgentMessage { Envelope = Envelopes.New(), Status = status },
                ct);

            await Task.Delay(interval, clock, ct);
        }
    }
}

public enum SessionEndKind
{
    StreamEnded,
    ControllerGoodbye
}

public sealed record AgentSessionOutcome(SessionEndKind Kind, GoodbyeReason GoodbyeReason);
