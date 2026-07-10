using ServerCenter.Core.Jobs;
using ServerCenter.Core.Recipes;

namespace ServerCenter.Agent.Linux;

// The idempotent ordered script runner (brief 3.12 - the one genuinely new primitive). For each step,
// in order: if its `alreadyDone` shell check passes, SKIP it (convergence - work already done is not
// redone); otherwise run it, and on success run its `onSuccess` marker (e.g. touch a sentinel) so the
// next apply skips it. This is what makes a recipe converge: build = repair = rebuild. A failed step
// stops the run (later steps may depend on it). Commands run via `sh -c` so shell syntax works.
public sealed class ScriptRunner(IProcessRunner runner)
{
    public async Task<ScriptRunOutcome> RunAsync(
        IReadOnlyList<RecipeScript> scripts, IJobSink sink, CancellationToken ct)
    {
        List<string> executed = new List<string>();
        List<string> skipped = new List<string>();

        foreach (RecipeScript script in scripts)
        {
            if (script.AlreadyDone is { } check && await ShellSucceedsAsync(check, ct))
            {
                sink.Log(LogStream.Note, $"skip {script.Id} (already done)");
                skipped.Add(script.Id);
                continue;
            }

            sink.Log(LogStream.Note, $"run {script.Id}: {script.Run}");
            ProcessResult result = await runner.RunAsync("sh", ["-c", script.Run], ct);
            if (result.ExitCode != 0)
            {
                return ScriptRunOutcome.Failed(
                    script.Id, $"script '{script.Id}' failed (exit {result.ExitCode}): {result.StandardError}", executed, skipped);
            }

            executed.Add(script.Id);

            if (script.OnSuccess is { } marker)
            {
                ProcessResult mark = await runner.RunAsync("sh", ["-c", marker], ct);
                if (mark.ExitCode != 0)
                {
                    return ScriptRunOutcome.Failed(
                        script.Id, $"onSuccess for '{script.Id}' failed (exit {mark.ExitCode}): {mark.StandardError}", executed, skipped);
                }
            }
        }

        return ScriptRunOutcome.Succeeded(executed, skipped);
    }

    private async Task<bool> ShellSucceedsAsync(string command, CancellationToken ct) =>
        (await runner.RunAsync("sh", ["-c", command], ct)).ExitCode == 0;
}

public sealed record ScriptRunOutcome(
    bool Success,
    string? FailedScriptId,
    string? FailReason,
    IReadOnlyList<string> Executed,
    IReadOnlyList<string> Skipped)
{
    public static ScriptRunOutcome Succeeded(IReadOnlyList<string> executed, IReadOnlyList<string> skipped) =>
        new(true, null, null, executed, skipped);

    public static ScriptRunOutcome Failed(
        string scriptId, string reason, IReadOnlyList<string> executed, IReadOnlyList<string> skipped) =>
        new(false, scriptId, reason, executed, skipped);
}
