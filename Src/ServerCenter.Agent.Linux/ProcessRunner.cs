using System.Diagnostics;

namespace ServerCenter.Agent.Linux;

// Real process execution (used on Linux nodes). Managing other units via systemctl requires
// privilege - the agent runs with the necessary rights (root or a scoped polkit rule); that is a
// deployment concern, not this code's.
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return new ProcessResult(process.ExitCode, (await stdout).Trim(), (await stderr).Trim());
    }
}
