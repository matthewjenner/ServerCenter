using ServerCenter.Core.Jobs;
using ServerCenter.Core.Transport;

namespace ServerCenter.Agent.Jobs;

// Streams a job's progress/log up to the controller as JobProgress messages, each with a
// monotonic per-job seq so the controller can order and ack them (phase-0-contracts.md 2.3).
// Sends are fire-and-forget; a dropped stream is fine because resync recovers.
public sealed class TransportJobSink(IAgentTransport transport, string jobId, CancellationToken ct) : IJobSink
{
    private long _seq;

    public long LastSeq => Interlocked.Read(ref _seq);

    public void Progress(int? pct, string? note) => Emit(new Contracts.V1.JobProgress
    {
        JobId = jobId,
        Seq = Interlocked.Increment(ref _seq),
        State = Contracts.V1.JobState.Running,
        Pct = pct ?? -1,
        Note = note ?? string.Empty
    });

    public void Log(LogStream stream, string line) => Emit(new Contracts.V1.JobProgress
    {
        JobId = jobId,
        Seq = Interlocked.Increment(ref _seq),
        State = Contracts.V1.JobState.Running,
        Pct = -1,
        Log = new Contracts.V1.LogLine { Stream = MapStream(stream), Line = line }
    });

    private void Emit(Contracts.V1.JobProgress progress) => _ = SendAsync(progress);

    private async Task SendAsync(Contracts.V1.JobProgress progress)
    {
        try
        {
            await transport.SendAsync(
                new Contracts.V1.AgentMessage { Envelope = Envelopes.New(), JobProgress = progress }, ct);
        }
        catch
        {
            // stream dropped mid-job; the controller reconciles on reconnect via resync
        }
    }

    private static Contracts.V1.LogStream MapStream(LogStream stream) => stream switch
    {
        LogStream.Stdout => Contracts.V1.LogStream.Stdout,
        LogStream.Stderr => Contracts.V1.LogStream.Stderr,
        _ => Contracts.V1.LogStream.Note
    };
}
