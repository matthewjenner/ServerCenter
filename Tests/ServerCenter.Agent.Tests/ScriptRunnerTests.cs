using AwesomeAssertions;
using ServerCenter.Agent.Linux;
using ServerCenter.Core.Recipes;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Agent.Tests;

// The convergence primitive: skip when alreadyDone passes, run + mark when it does not, stop on
// failure. Commands go through `sh -c`; a fake process runner answers by exit code per command.
public sealed class ScriptRunnerTests
{
    // Maps a shell command (args[1] of `sh -c <cmd>`) to an exit code; unmapped commands succeed.
    private static FakeProcessRunner Runner(Dictionary<string, int> exitByCommand) => new()
    {
        Respond = (_, args) => new ProcessResult(
            args.Count > 1 && exitByCommand.TryGetValue(args[1], out int code) ? code : 0, string.Empty, string.Empty)
    };

    [Fact]
    public async Task Skips_a_step_whose_already_done_check_passes()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        // alreadyDone "check" passes (exit 0) -> the step is already done.
        FakeProcessRunner runner = Runner(new Dictionary<string, int> { ["check"] = 0 });
        RecipeScript[] scripts = new[] { new RecipeScript("s1", "do-work", AlreadyDone: "check", OnSuccess: "mark") };

        ScriptRunOutcome outcome = await new ScriptRunner(runner).RunAsync(scripts, new RecordingJobSink(), ct);

        outcome.Success.Should().BeTrue();
        outcome.Skipped.Should().Equal("s1");
        outcome.Executed.Should().BeEmpty();
        runner.Invocations.Should().NotContain(i => i.Args.Contains("do-work")); // work was not redone
    }

    [Fact]
    public async Task Runs_and_marks_a_step_that_is_not_done()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        // alreadyDone "check" fails (exit 1) -> not done -> run + onSuccess.
        FakeProcessRunner runner = Runner(new Dictionary<string, int> { ["check"] = 1 });
        RecipeScript[] scripts = new[] { new RecipeScript("s1", "do-work", AlreadyDone: "check", OnSuccess: "mark") };

        ScriptRunOutcome outcome = await new ScriptRunner(runner).RunAsync(scripts, new RecordingJobSink(), ct);

        outcome.Success.Should().BeTrue();
        outcome.Executed.Should().Equal("s1");
        runner.Invocations.Should().Contain(i => i.Args.Contains("do-work"));
        runner.Invocations.Should().Contain(i => i.Args.Contains("mark")); // marker ran
    }

    [Fact]
    public async Task A_step_with_no_already_done_always_runs()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeProcessRunner runner = Runner([]);
        RecipeScript[] scripts = new[] { new RecipeScript("s1", "always") };

        ScriptRunOutcome outcome = await new ScriptRunner(runner).RunAsync(scripts, new RecordingJobSink(), ct);

        outcome.Executed.Should().Equal("s1");
    }

    [Fact]
    public async Task A_failing_step_stops_the_run()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeProcessRunner runner = Runner(new Dictionary<string, int> { ["boom"] = 2 });
        RecipeScript[] scripts = new[]
        {
            new RecipeScript("s1", "boom"),
            new RecipeScript("s2", "never")
        };

        ScriptRunOutcome outcome = await new ScriptRunner(runner).RunAsync(scripts, new RecordingJobSink(), ct);

        outcome.Success.Should().BeFalse();
        outcome.FailedScriptId.Should().Be("s1");
        outcome.Executed.Should().BeEmpty();
        runner.Invocations.Should().NotContain(i => i.Args.Contains("never")); // s2 never ran
    }
}
