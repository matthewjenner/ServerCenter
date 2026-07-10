using ServerCenter.Contracts.V1;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Transport;

namespace ServerCenter.Core.Connection;

// The controller side of the connect handshake. Reads the agent's Hello, negotiates the
// protocol version (Goodbye on mismatch), sends HelloAck, then always runs the resync
// handshake (phase-0-contracts.md 2.3) - built before any real job exists, so an empty resync
// round-trips cleanly. Returns the established session and the reconcile actions applied.
//
// This is the handshake only. The steady-state pump (heartbeat/status ingest, command push)
// is a later Phase 1 ship; this method returns once the session is established or rejected.
public static class ControllerHandshake
{
    public static async Task<ControllerHandshakeResult> PerformAsync(
        IControllerStream stream,
        IControllerJobView jobs,
        Func<string>? sessionIdFactory = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(jobs);
        sessionIdFactory ??= () => Guid.NewGuid().ToString("N");

        await using IAsyncEnumerator<AgentMessage> incoming = stream.Incoming(ct).GetAsyncEnumerator(ct);

        if (!await incoming.MoveNextAsync())
        {
            return ControllerHandshakeResult.NotEstablished("stream closed before Hello");
        }

        AgentMessage first = incoming.Current;
        if (first.PayloadCase != AgentMessage.PayloadOneofCase.Hello)
        {
            return ControllerHandshakeResult.NotEstablished($"expected Hello, got {first.PayloadCase}");
        }

        HandshakeDecision decision = HandshakeNegotiator.Decide(
            first.Envelope.ProtocolMajor,
            first.Envelope.ProtocolMinor,
            sessionIdFactory);

        if (!decision.Accepted)
        {
            await stream.SendAsync(
                new ControllerMessage
                {
                    Envelope = Envelopes.New(first.Envelope.MessageId),
                    Goodbye = new Goodbye
                    {
                        Reason = GoodbyeReason.VersionMismatch,
                        Message = decision.RejectReason
                    }
                },
                ct);

            return ControllerHandshakeResult.NotEstablished(decision.RejectReason);
        }

        string agentId = first.Hello.AgentId;
        IReadOnlyList<ControllerOpenJob> open = await jobs.GetOpenJobsAsync(agentId, ct);

        await stream.SendAsync(
            new ControllerMessage
            {
                Envelope = Envelopes.New(first.Envelope.MessageId),
                HelloAck = new HelloAck
                {
                    NegotiatedMinor = decision.NegotiatedMinor,
                    SessionId = decision.SessionId,
                    WantsResync = true
                }
            },
            ct);

        ResyncRequest resyncRequest = new ResyncRequest();
        resyncRequest.OpenJobIds.AddRange(open.Select(o => o.JobId));
        await stream.SendAsync(
            new ControllerMessage { Envelope = Envelopes.New(), ResyncRequest = resyncRequest },
            ct);

        if (!await incoming.MoveNextAsync())
        {
            return ControllerHandshakeResult.NotEstablished("stream closed before JobResyncReport");
        }

        AgentMessage reportMsg = incoming.Current;
        if (reportMsg.PayloadCase != AgentMessage.PayloadOneofCase.JobResync)
        {
            return ControllerHandshakeResult.NotEstablished($"expected JobResyncReport, got {reportMsg.PayloadCase}");
        }

        List<AgentResyncEntry> entries = reportMsg.JobResync.Entries
            .Select(e => new AgentResyncEntry(e.JobId, MapLocalState(e.LocalState), e.LastSeq))
            .ToList();

        IReadOnlyList<ReconcileAction> actions = JobResyncReconciler.Reconcile(open, entries);
        foreach (ReconcileAction action in actions)
        {
            await jobs.ApplyAsync(action, ct);
        }

        return new ControllerHandshakeResult
        {
            Established = true,
            SessionId = decision.SessionId,
            AgentId = agentId,
            NodeKind = string.IsNullOrEmpty(first.Hello.NodeKind) ? "guest" : first.Hello.NodeKind,
            ReconcileActions = actions
        };
    }

    private static AgentJobLocalState MapLocalState(ResyncLocalState state) => state switch
    {
        ResyncLocalState.StillRunning => AgentJobLocalState.StillRunning,
        ResyncLocalState.FinishedSucceeded => AgentJobLocalState.FinishedSucceeded,
        ResyncLocalState.FinishedFailed => AgentJobLocalState.FinishedFailed,
        _ => AgentJobLocalState.Unknown
    };
}

public sealed record ControllerHandshakeResult
{
    public required bool Established { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public string NodeKind { get; init; } = "guest";
    public string RejectReason { get; init; } = string.Empty;
    public IReadOnlyList<ReconcileAction> ReconcileActions { get; init; } = Array.Empty<ReconcileAction>();

    public static ControllerHandshakeResult NotEstablished(string reason) =>
        new() { Established = false, RejectReason = reason };
}
