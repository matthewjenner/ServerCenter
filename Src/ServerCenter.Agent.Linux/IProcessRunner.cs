namespace ServerCenter.Agent.Linux;

// Runs an external process and captures its result. Behind an interface so the Linux service
// control logic (command construction + output parsing) is unit-testable without a real systemd.
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct);
}

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
