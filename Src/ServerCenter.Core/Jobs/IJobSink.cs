namespace ServerCenter.Core.Jobs;

// How any long-running operation reports into the job spine. The sink owns seq numbering and
// local buffering for resync (phase-0-contracts.md 2.3), so callers just emit.
public interface IJobSink
{
    void Progress(int? pct, string? note);

    void Log(LogStream stream, string line);
}

public enum LogStream
{
    Stdout,
    Stderr,
    Note
}
