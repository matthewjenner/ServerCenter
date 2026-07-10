using AwesomeAssertions;
using ServerCenter.Agent.Jobs;
using ServerCenter.Agent.Linux;
using ServerCenter.Core.Games;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Primitives;
using ServerCenter.Core.Recipes;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Agent.Tests;

// recipe.apply composition: each declared section runs, in order, against fakes; a failed section
// fails the job and stops. (Per-step convergence is the ScriptRunner's own test.)
public sealed class RecipeApplyExecutorTests
{
    private sealed record Harness(
        FakePackageInstaller Packages,
        FakeSteamCmd Steam,
        RecordingConfigWriter Writer,
        FakeServiceController Services,
        FakeProcessRunner ScriptRunnerProcess)
    {
        public RecipeApplyExecutor Executor() =>
            new(Packages, Steam, Writer, new ScriptRunner(ScriptRunnerProcess), Services);
    }

    private static Harness NewHarness() =>
        new(new FakePackageInstaller(), new FakeSteamCmd(), new RecordingConfigWriter(),
            new FakeServiceController(), new FakeProcessRunner());

    private static JobContext Context(BuildRecipe recipe, IReadOnlyDictionary<string, string>? templates = null) =>
        new("j1", "recipe.apply", RecipeApplyParamsSerializer.Serialize(
            new RecipeApplyParams(recipe, new Dictionary<string, string> { ["name"] = "ffa" },
                templates ?? new Dictionary<string, string>())));

    private static readonly BuildRecipe FullRecipe = new()
    {
        Id = "cs2-server",
        Version = 5,
        BaseRequirements = new BaseRequirements("apt", ["steamcmd"]),
        SteamApp = new SteamAppSpec(730, "/opt/cs2"),
        ConfigFiles = [new ConfigFileSpec("cs2/server.cfg", "/opt/cs2/cfg/server.cfg", ConfigFormat.Kv)],
        Scripts = [new RecipeScript("s1", "setup")],
        ServiceDefinition = new ServiceDefinition("cs2.service", "/opt/cs2/start.sh", "cs2")
    };

    [Fact]
    public async Task Applies_every_section_of_the_recipe()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Harness h = NewHarness();
        Dictionary<string, string> templates = new Dictionary<string, string> { ["cs2/server.cfg"] = "hostname={{name}}" };

        JobOutcome outcome = await h.Executor().ExecuteAsync(Context(FullRecipe, templates), new RecordingJobSink(), ct);

        outcome.Succeeded.Should().BeTrue();
        h.Packages.Installed.Should().Contain("steamcmd");
        h.Steam.Requests.Should().ContainSingle().Which.AppId.Should().Be(730);
        h.Writer.Writes.Should().Contain(w => w.Path == "/opt/cs2/cfg/server.cfg" && w.Content == "hostname=ffa");
        h.Writer.Writes.Should().Contain(w => w.Path == "/etc/systemd/system/cs2.service"); // unit file written
        h.Services.Calls.Should().Contain(("daemon-reload", string.Empty));
        h.Services.Calls.Should().Contain(("enable", "cs2.service"));
        h.Services.Calls.Should().Contain(("start", "cs2.service"));
    }

    [Fact]
    public async Task A_steam_failure_fails_the_job_before_config()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Harness h = NewHarness() with { Steam = new FakeSteamCmd { Result = new SteamAppResult(false, null, "boom") } };
        // Rebuild harness fields consistently (record 'with' keeps the others).

        JobOutcome outcome = await h.Executor().ExecuteAsync(
            Context(FullRecipe, new Dictionary<string, string> { ["cs2/server.cfg"] = "hostname={{name}}" }),
            new RecordingJobSink(), ct);

        outcome.Succeeded.Should().BeFalse();
        h.Writer.Writes.Should().BeEmpty(); // config never ran
    }

    [Fact]
    public async Task An_unsupported_package_provider_fails()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Harness h = NewHarness();
        BuildRecipe recipe = FullRecipe with { BaseRequirements = new BaseRequirements("yum", ["x"]) };

        JobOutcome outcome = await h.Executor().ExecuteAsync(Context(recipe), new RecordingJobSink(), ct);

        outcome.Succeeded.Should().BeFalse();
        outcome.FailReason.Should().Contain("unsupported package provider");
    }

    [Fact]
    public async Task A_config_only_recipe_writes_config_and_nothing_else()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Harness h = NewHarness();
        BuildRecipe recipe = new BuildRecipe
        {
            Id = "config-only",
            Version = 1,
            ConfigFiles = [new ConfigFileSpec("cs2/server.cfg", "/opt/cs2/cfg/server.cfg", ConfigFormat.Kv)]
        };

        JobOutcome outcome = await h.Executor().ExecuteAsync(
            Context(recipe, new Dictionary<string, string> { ["cs2/server.cfg"] = "hostname={{name}}" }),
            new RecordingJobSink(), ct);

        outcome.Succeeded.Should().BeTrue();
        h.Steam.Requests.Should().BeEmpty();
        h.Services.Calls.Should().BeEmpty();
        h.Writer.Writes.Should().ContainSingle();
    }
}
