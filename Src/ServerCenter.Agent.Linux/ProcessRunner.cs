using System.Diagnostics;

namespace ServerCenter.Agent.Linux;

// Real process execution (used on Linux nodes). Managing other units via systemctl, and running
// apt/dpkg, requires privilege - the agent runs with the necessary rights (root or a scoped polkit
// rule); that is a deployment concern, not this code's.
public sealed class ProcessRunner : IProcessRunner
{
    private static readonly Dictionary<string, string> NoEnvironment = new();

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct) =>
        RunAsync(fileName, arguments, NoEnvironment, ct);

    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken ct)
    {
        using Process process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        foreach ((string? key, string? value) in environment)
        {
            process.StartInfo.Environment[key] = value;
        }

        process.Start();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(ct);
        Task<string> stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return new ProcessResult(process.ExitCode, (await stdout).Trim(), (await stderr).Trim());
    }
}
