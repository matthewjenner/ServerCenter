using ServerCenter.Contracts.V1;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Transport;

namespace ServerCenter.Core.Connection;

// The agent side of the connect handshake. Sends Hello, handles the reply (Goodbye =
// rejected, e.g. version mismatch; HelloAck = accepted), and if the controller wants a resync
// replies with its local job state. Returns the negotiated session id. The reconnect-with-
// backoff loop that repeatedly invokes this is a later Phase 1 ship.
public static class AgentHandshake
{
    public static async Task<AgentHandshakeResult> PerformAsync(
        IAgentTransport transport,
        AgentIdentity identity,
        IAgentJobStateSource jobs,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(jobs);

        var hello = new Hello
        {
            AgentId = identity.AgentId,
            AgentVersion = identity.AgentVersion,
            OsFamily = identity.OsFamily,
            Arch = identity.Arch,
            NodeKind = identity.NodeKind
        };
        var local = await jobs.GetLocalJobStateAsync(ct);
        hello.InFlightJobIds.AddRange(local.Select(e => e.JobId));

        await transport.SendAsync(
            new AgentMessage { Envelope = Envelopes.New(), Hello = hello },
            ct);

        await using var incoming = transport.Incoming(ct).GetAsyncEnumerator(ct);

        if (!await incoming.MoveNextAsync())
        {
            return AgentHandshakeResult.Rejected("stream closed before HelloAck");
        }

        var reply = incoming.Current;
        if (reply.PayloadCase == ControllerMessage.PayloadOneofCase.Goodbye)
        {
            var reason = reply.Goodbye.Reason;
            var terminal = reason is GoodbyeReason.VersionMismatch or GoodbyeReason.Revoked;
            return AgentHandshakeResult.Rejected($"{reason}: {reply.Goodbye.Message}", terminal);
        }

        if (reply.PayloadCase != ControllerMessage.PayloadOneofCase.HelloAck)
        {
            return AgentHandshakeResult.Rejected($"expected HelloAck, got {reply.PayloadCase}");
        }

        var ack = reply.HelloAck;

        if (ack.WantsResync)
        {
            if (!await incoming.MoveNextAsync() ||
                incoming.Current.PayloadCase != ControllerMessage.PayloadOneofCase.ResyncRequest)
            {
                return AgentHandshakeResult.Rejected("expected ResyncRequest after HelloAck");
            }

            var report = new JobResyncReport();
            foreach (var entry in local)
            {
                report.Entries.Add(new JobResyncEntry
                {
                    JobId = entry.JobId,
                    LocalState = MapLocalState(entry.LocalState),
                    LastSeq = entry.LastSeq
                });
            }

            await transport.SendAsync(
                new AgentMessage { Envelope = Envelopes.New(), JobResync = report },
                ct);
        }

        return new AgentHandshakeResult { Established = true, SessionId = ack.SessionId };
    }

    private static ResyncLocalState MapLocalState(AgentJobLocalState state) => state switch
    {
        AgentJobLocalState.StillRunning => ResyncLocalState.StillRunning,
        AgentJobLocalState.FinishedSucceeded => ResyncLocalState.FinishedSucceeded,
        AgentJobLocalState.FinishedFailed => ResyncLocalState.FinishedFailed,
        _ => ResyncLocalState.Unknown
    };
}

public sealed record AgentHandshakeResult
{
    public required bool Established { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public string RejectReason { get; init; } = string.Empty;

    // True when the controller rejected us permanently (version mismatch / revoked): the dial
    // loop must stop rather than reconnect.
    public bool Terminal { get; init; }

    public static AgentHandshakeResult Rejected(string reason, bool terminal = false) =>
        new() { Established = false, RejectReason = reason, Terminal = terminal };
}
