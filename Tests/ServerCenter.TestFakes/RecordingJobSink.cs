using ServerCenter.Core.Jobs;

namespace ServerCenter.TestFakes;

// Records what an executor/provider streams into the job spine, so tests can assert on progress and
// log output without a real transport.
public sealed class RecordingJobSink : IJobSink
{
    public List<(int? Pct, string? Note)> Progresses { get; } = [];

    public List<(LogStream Stream, string Line)> Logs { get; } = [];

    public void Progress(int? pct, string? note) => Progresses.Add((pct, note));

    public void Log(LogStream stream, string line) => Logs.Add((stream, line));
}
