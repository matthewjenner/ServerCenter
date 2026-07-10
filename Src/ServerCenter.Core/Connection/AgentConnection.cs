using ServerCenter.Contracts.V1;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Transport;

namespace ServerCenter.Core.Connection;

// The agent's outbound dial loop: connect, handshake, run the session pump, and on any session
// end reconnect with backoff - unless the controller told us to go away for good (version
// mismatch / revoked). Stream established = online; stream dropped = offline, then retry. The
// connect delegate is where the real gRPC channel is dialed (a later Phase 1 ship); tests
// inject an in-memory transport factory.
public static class AgentConnection
{
    public static async Task RunAsync(
        Func<CancellationToken, Task<IAgentTransport>> connect,
        AgentIdentity identity,
        IAgentJobStateSource jobs,
        IAgentStatusSource status,
        IAgentCommandHandler commands,
        TimeProvider clock,
        AgentConnectionOptions options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connect);
        ArgumentNullException.ThrowIfNull(options);

        var attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var transport = await connect(ct);
                var handshake = await AgentHandshake.PerformAsync(transport, identity, jobs, ct);

                if (handshake.Established)
                {
                    attempt = 0; // reset backoff on a good connection
                    var outcome = await AgentSessionPump.RunAsync(
                        transport, status, commands, clock, options.HeartbeatInterval, ct);

                    if (outcome.Kind == SessionEndKind.ControllerGoodbye && IsTerminal(outcome.GoodbyeReason))
                    {
                        return;
                    }
                }
                else if (handshake.Terminal)
                {
                    return; // permanently rejected: do not retry
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // transient connect / handshake / session failure: fall through to backoff
            }

            if (ct.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await Task.Delay(options.Backoff.NextDelay(attempt++), clock, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static bool IsTerminal(GoodbyeReason reason) =>
        reason is GoodbyeReason.VersionMismatch or GoodbyeReason.Revoked;
}

public sealed record AgentConnectionOptions(TimeSpan HeartbeatInterval, BackoffPolicy Backoff);
