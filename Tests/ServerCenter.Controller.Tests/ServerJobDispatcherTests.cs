using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Games;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Recipes;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Controller.Tests;

// The controller-side descriptor/recipe-driven dispatch (real temp SQLite). It resolves the instance
// + its pinned descriptor/recipe and dispatches server.install / server.config-apply / recipe.apply.
public sealed class ServerJobDispatcherTests : IAsyncLifetime
{
    private TempDatabase _db = null!;
    private GameDescriptorRepository _descriptors = null!;
    private BuildRecipeRepository _recipes = null!;
    private ServerInstanceRepository _instances = null!;
    private JobRepository _jobs = null!;
    private ServerJobDispatcher _dispatcher = null!;

    public async ValueTask InitializeAsync()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _db = await TempDatabase.CreateAsync(ct);
        _descriptors = new GameDescriptorRepository(_db.Database);
        _recipes = new BuildRecipeRepository(_db.Database);
        _instances = new ServerInstanceRepository(_db.Database);
        _jobs = new JobRepository(_db.Database);

        AgentNodeRepository nodes = new AgentNodeRepository(_db.Database);
        await nodes.EnsureAgentAsync("agent-1", "agent-1", "fpr", 1, ct);
        await nodes.EnsureNodeAsync("agent-1", "agent-1", "guest", "managed", 1, ct);

        JobDispatcher jobDispatcher = new JobDispatcher(_jobs, new ConnectedAgents(), new FakeTimeProvider());
        FakeConfigTemplateSource templates = new FakeConfigTemplateSource(
            new Dictionary<string, string> { ["cs2/server.cfg"] = "hostname={{name}}" });
        _dispatcher = new ServerJobDispatcher(_descriptors, _recipes, _instances, jobDispatcher, templates);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    private static GameDescriptor Descriptor(bool withConfigGen) => new()
    {
        Id = "cs2-dedicated",
        Version = 3,
        SteamApp = new SteamAppSpec(730, "/opt/cs2"),
        Capabilities = withConfigGen
            ? new GameCapabilities
            {
                ConfigGen = new ConfigGenSpec("config-template",
                    [new ConfigFileSpec("cs2/server.cfg", "/opt/cs2/cfg/server.cfg", ConfigFormat.Kv)])
            }
            : new GameCapabilities()
    };

    private async Task SeedAsync(bool withConfigGen)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await _descriptors.InsertAsync(Descriptor(withConfigGen), 1000, ct);
        await _instances.InsertAsync(new ServerInstance
        {
            Id = "srv-1",
            NodeId = "agent-1",
            DescriptorId = "cs2-dedicated",
            DescriptorVersion = 3,
            InstanceParamsJson = """{"name":"ffa","ports":{"game":27015}}""",
            CreatedAtUnixMs = 1000
        }, ct);
    }

    [Fact]
    public async Task Install_dispatches_a_server_install_from_the_descriptor_app()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SeedAsync(withConfigGen: false);

        ServerDispatchResult result = await _dispatcher.InstallAsync("agent-1", "srv-1", ct);

        result.Outcome.Should().Be(ServerDispatchOutcome.Dispatched);
        Job? job = await _jobs.GetAsync(result.JobId!, ct);
        job!.Type.Should().Be("server.install");
        ServerInstallParams request = ServerJobParamsSerializer.Deserialize<ServerInstallParams>(job.ParamsJson);
        request.AppId.Should().Be(730);
        request.InstallDir.Should().Be("/opt/cs2");
    }

    [Fact]
    public async Task Install_reports_a_missing_instance()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        ServerDispatchResult result = await _dispatcher.InstallAsync("agent-1", "nope", ct);

        result.Outcome.Should().Be(ServerDispatchOutcome.NotFound);
    }

    [Fact]
    public async Task ConfigApply_ships_the_resolved_template_and_flattened_params()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SeedAsync(withConfigGen: true);

        ServerDispatchResult result = await _dispatcher.ConfigApplyAsync("agent-1", "srv-1", ct);

        result.Outcome.Should().Be(ServerDispatchOutcome.Dispatched);
        Job? job = await _jobs.GetAsync(result.JobId!, ct);
        job!.Type.Should().Be("server.config-apply");
        ServerConfigApplyParams request = ServerJobParamsSerializer.Deserialize<ServerConfigApplyParams>(job.ParamsJson);
        request.Templates["cs2/server.cfg"].Should().Be("hostname={{name}}");
        request.InstanceParams["ports.game"].Should().Be("27015");
    }

    [Fact]
    public async Task ConfigApply_reports_a_descriptor_without_config_gen()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SeedAsync(withConfigGen: false);

        ServerDispatchResult result = await _dispatcher.ConfigApplyAsync("agent-1", "srv-1", ct);

        result.Outcome.Should().Be(ServerDispatchOutcome.NotConfigured);
    }

    [Fact]
    public async Task ApplyRecipe_resolves_the_pinned_recipe_and_ships_its_templates()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await _recipes.InsertAsync(new BuildRecipe
        {
            Id = "cs2-server",
            Version = 5,
            SteamApp = new SteamAppSpec(730, "/opt/cs2"),
            ConfigFiles = [new ConfigFileSpec("cs2/server.cfg", "/opt/cs2/cfg/server.cfg", ConfigFormat.Kv)]
        }, 1000, ct);
        await _instances.InsertAsync(new ServerInstance
        {
            Id = "srv-1",
            NodeId = "agent-1",
            RecipeId = "cs2-server",
            RecipeVersion = 5,
            InstanceParamsJson = """{"name":"ffa"}""",
            CreatedAtUnixMs = 1000
        }, ct);

        ServerDispatchResult result = await _dispatcher.ApplyRecipeAsync("agent-1", "srv-1", ct);

        result.Outcome.Should().Be(ServerDispatchOutcome.Dispatched);
        Job? job = await _jobs.GetAsync(result.JobId!, ct);
        job!.Type.Should().Be("recipe.apply");
        RecipeApplyParams request = RecipeApplyParamsSerializer.Deserialize(job.ParamsJson);
        request.Recipe.Version.Should().Be(5);
        request.Templates["cs2/server.cfg"].Should().Be("hostname={{name}}");
        request.InstanceParams["name"].Should().Be("ffa");
    }

    [Fact]
    public async Task ApplyRecipe_reports_an_instance_with_no_recipe()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SeedAsync(withConfigGen: false); // seeds an instance with a descriptor but no recipe

        ServerDispatchResult result = await _dispatcher.ApplyRecipeAsync("agent-1", "srv-1", ct);

        result.Outcome.Should().Be(ServerDispatchOutcome.NotConfigured);
    }
}
