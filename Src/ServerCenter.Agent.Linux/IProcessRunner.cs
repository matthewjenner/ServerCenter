namespace ServerCenter.Agent.Linux;

// Runs an external process and captures its result. Behind an interface so command construction and
// output parsing (systemctl, apt-get, dpkg) are unit-testable without a real Linux box. The env
// overload exists for tools that need it (apt/dpkg want DEBIAN_FRONTEND=noninteractive so a headless
// run never blocks on a debconf prompt); systemctl callers use the plain overload.
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct);

    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken ct);
}

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
