using System.Text.Json;
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

    // Two instances of the SAME descriptor get DISTINCT install dirs from the {{instance.id}} template.
    [Fact]
    public async Task Install_renders_a_per_instance_install_dir()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await _descriptors.InsertAsync(new GameDescriptor
        {
            Id = "cs2-dedicated",
            Version = 3,
            SteamApp = new SteamAppSpec(730, "/opt/servercenter/cs2/{{instance.id}}")
        }, 1000, ct);
        await InsertInstanceAsync("arena1", ct, descriptorVersion: 3, portGame: 27015);
        await InsertInstanceAsync("arena2", ct, descriptorVersion: 3, portGame: 27017);

        ServerInstallParams p1 = await InstallParamsAsync("arena1", ct);
        ServerInstallParams p2 = await InstallParamsAsync("arena2", ct);

        p1.InstallDir.Should().Be("/opt/servercenter/cs2/arena1");
        p2.InstallDir.Should().Be("/opt/servercenter/cs2/arena2");
    }

    // The recipe's unit name, ExecStart, and config path are all rendered per-instance so two CS2
    // servers on one VM coexist. instance.dir (the rendered install dir) is referenceable downstream.
    [Fact]
    public async Task ApplyRecipe_renders_per_instance_unit_execstart_and_config_path()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await _recipes.InsertAsync(new BuildRecipe
        {
            Id = "cs2-server",
            Version = 5,
            SteamApp = new SteamAppSpec(730, "/opt/servercenter/cs2/{{instance.id}}"),
            ConfigFiles = [new ConfigFileSpec("cs2/server.cfg", "{{instance.dir}}/game/cfg/server.cfg", ConfigFormat.Kv)],
            ServiceDefinition = new ServiceDefinition(
                "sc-cs2-{{instance.id}}.service", "{{instance.dir}}/game/cs2.sh -port {{ports.game}}")
        }, 1000, ct);
        await InsertInstanceAsync("arena1", ct, recipeVersion: 5, portGame: 27015);
        await InsertInstanceAsync("arena2", ct, recipeVersion: 5, portGame: 27017);

        RecipeApplyParams a1 = await RecipeParamsAsync("arena1", ct);
        RecipeApplyParams a2 = await RecipeParamsAsync("arena2", ct);

        a1.Recipe.SteamApp!.InstallDir.Should().Be("/opt/servercenter/cs2/arena1");
        a1.Recipe.ServiceDefinition!.Unit.Should().Be("sc-cs2-arena1.service");
        a1.Recipe.ServiceDefinition!.ExecStart.Should().Be("/opt/servercenter/cs2/arena1/game/cs2.sh -port 27015");
        a1.Recipe.ConfigFiles[0].Path.Should().Be("/opt/servercenter/cs2/arena1/game/cfg/server.cfg");

        a2.Recipe.ServiceDefinition!.Unit.Should().Be("sc-cs2-arena2.service");
        a2.Recipe.ServiceDefinition!.ExecStart.Should().Be("/opt/servercenter/cs2/arena2/game/cs2.sh -port 27017");

        // The whole point: distinct units so systemd runs both.
        a1.Recipe.ServiceDefinition!.Unit.Should().NotBe(a2.Recipe.ServiceDefinition!.Unit);
    }

    [Fact]
    public async Task Install_reports_an_unknown_template_token()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await _descriptors.InsertAsync(new GameDescriptor
        {
            Id = "cs2-dedicated",
            Version = 3,
            SteamApp = new SteamAppSpec(730, "/opt/cs2/{{does.not.exist}}")
        }, 1000, ct);
        await InsertInstanceAsync("arena1", ct, descriptorVersion: 3, portGame: 27015);

        ServerDispatchResult result = await _dispatcher.InstallAsync("agent-1", "arena1", ct);

        result.Outcome.Should().Be(ServerDispatchOutcome.NotConfigured);
        result.Reason.Should().Contain("does.not.exist");
    }

    // Remove renders the per-instance unit / install dir / config paths from the pinned descriptor+recipe.
    [Fact]
    public async Task Remove_renders_per_instance_unit_dir_and_config_paths()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await _descriptors.InsertAsync(new GameDescriptor
        {
            Id = "cs2-dedicated",
            Version = 3,
            SteamApp = new SteamAppSpec(730, "/opt/servercenter/cs2/{{instance.id}}"),
            Capabilities = new GameCapabilities
            {
                ConfigGen = new ConfigGenSpec("config-template",
                    [new ConfigFileSpec("cs2/server.cfg", "{{instance.dir}}/cfg/server.cfg", ConfigFormat.Kv)])
            }
        }, 1000, ct);
        await _recipes.InsertAsync(new BuildRecipe
        {
            Id = "cs2-server",
            Version = 5,
            SteamApp = new SteamAppSpec(730, "/opt/servercenter/cs2/{{instance.id}}"),
            ServiceDefinition = new ServiceDefinition("sc-cs2-{{instance.id}}.service", "{{instance.dir}}/cs2.sh")
        }, 1000, ct);
        await _instances.InsertAsync(new ServerInstance
        {
            Id = "arena1",
            NodeId = "agent-1",
            DescriptorId = "cs2-dedicated",
            DescriptorVersion = 3,
            RecipeId = "cs2-server",
            RecipeVersion = 5,
            InstanceParamsJson = """{"name":"arena1"}""",
            CreatedAtUnixMs = 1000
        }, ct);

        ServerDispatchResult result = await _dispatcher.RemoveAsync("arena1", ct);

        result.Outcome.Should().Be(ServerDispatchOutcome.Dispatched);
        Job job = (await _jobs.GetAsync(result.JobId!, ct))!;
        job.Type.Should().Be("server.remove");
        ServerRemoveParams p = ServerJobParamsSerializer.Deserialize<ServerRemoveParams>(job.ParamsJson);
        p.Unit.Should().Be("sc-cs2-arena1.service");
        p.InstallDir.Should().Be("/opt/servercenter/cs2/arena1");
        p.ConfigPaths.Should().Contain("/opt/servercenter/cs2/arena1/cfg/server.cfg");
    }

    [Fact]
    public async Task Remove_reports_a_missing_instance()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        ServerDispatchResult result = await _dispatcher.RemoveAsync("nope", ct);

        result.Outcome.Should().Be(ServerDispatchOutcome.NotFound);
    }

    private async Task InsertInstanceAsync(string id, CancellationToken ct, int? descriptorVersion = null, int? recipeVersion = null, int portGame = 27015)
    {
        await _instances.InsertAsync(new ServerInstance
        {
            Id = id,
            NodeId = "agent-1",
            DescriptorId = descriptorVersion is null ? null : "cs2-dedicated",
            DescriptorVersion = descriptorVersion,
            RecipeId = recipeVersion is null ? null : "cs2-server",
            RecipeVersion = recipeVersion,
            InstanceParamsJson = JsonSerializer.Serialize(new { name = id, ports = new { game = portGame } }),
            CreatedAtUnixMs = 1000
        }, ct);
    }

    private async Task<ServerInstallParams> InstallParamsAsync(string instanceId, CancellationToken ct)
    {
        ServerDispatchResult result = await _dispatcher.InstallAsync("agent-1", instanceId, ct);
        result.Outcome.Should().Be(ServerDispatchOutcome.Dispatched);
        Job job = (await _jobs.GetAsync(result.JobId!, ct))!;
        return ServerJobParamsSerializer.Deserialize<ServerInstallParams>(job.ParamsJson);
    }

    private async Task<RecipeApplyParams> RecipeParamsAsync(string instanceId, CancellationToken ct)
    {
        ServerDispatchResult result = await _dispatcher.ApplyRecipeAsync("agent-1", instanceId, ct);
        result.Outcome.Should().Be(ServerDispatchOutcome.Dispatched);
        Job job = (await _jobs.GetAsync(result.JobId!, ct))!;
        return RecipeApplyParamsSerializer.Deserialize(job.ParamsJson);
    }
}
