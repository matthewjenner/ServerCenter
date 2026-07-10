using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Games;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Controller.Tests;

// The controller-side descriptor-driven dispatch (real temp SQLite). It resolves the instance + its
// pinned descriptor and dispatches server.install / server.config-apply with the resolved params.
public sealed class ServerJobDispatcherTests : IAsyncLifetime
{
    private TempDatabase _db = null!;
    private GameDescriptorRepository _descriptors = null!;
    private ServerInstanceRepository _instances = null!;
    private JobRepository _jobs = null!;
    private ServerJobDispatcher _dispatcher = null!;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        _db = await TempDatabase.CreateAsync(ct);
        _descriptors = new GameDescriptorRepository(_db.Database);
        _instances = new ServerInstanceRepository(_db.Database);
        _jobs = new JobRepository(_db.Database);

        var nodes = new AgentNodeRepository(_db.Database);
        await nodes.EnsureAgentAsync("agent-1", "agent-1", "fpr", 1, ct);
        await nodes.EnsureNodeAsync("agent-1", "agent-1", "guest", "managed", 1, ct);

        var jobDispatcher = new JobDispatcher(_jobs, new ConnectedAgents(), new FakeTimeProvider());
        var templates = new FakeConfigTemplateSource(
            new Dictionary<string, string> { ["cs2/server.cfg"] = "hostname={{name}}" });
        _dispatcher = new ServerJobDispatcher(_descriptors, _instances, jobDispatcher, templates);
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
        var ct = TestContext.Current.CancellationToken;
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
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(withConfigGen: false);

        var result = await _dispatcher.InstallAsync("agent-1", "srv-1", ct);

        result.Outcome.Should().Be(ServerDispatchOutcome.Dispatched);
        var job = await _jobs.GetAsync(result.JobId!, ct);
        job!.Type.Should().Be("server.install");
        var request = ServerJobParamsSerializer.Deserialize<ServerInstallParams>(job.ParamsJson);
        request.AppId.Should().Be(730);
        request.InstallDir.Should().Be("/opt/cs2");
    }

    [Fact]
    public async Task Install_reports_a_missing_instance()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await _dispatcher.InstallAsync("agent-1", "nope", ct);

        result.Outcome.Should().Be(ServerDispatchOutcome.NotFound);
    }

    [Fact]
    public async Task ConfigApply_ships_the_resolved_template_and_flattened_params()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(withConfigGen: true);

        var result = await _dispatcher.ConfigApplyAsync("agent-1", "srv-1", ct);

        result.Outcome.Should().Be(ServerDispatchOutcome.Dispatched);
        var job = await _jobs.GetAsync(result.JobId!, ct);
        job!.Type.Should().Be("server.config-apply");
        var request = ServerJobParamsSerializer.Deserialize<ServerConfigApplyParams>(job.ParamsJson);
        request.Templates["cs2/server.cfg"].Should().Be("hostname={{name}}");
        request.InstanceParams["ports.game"].Should().Be("27015");
    }

    [Fact]
    public async Task ConfigApply_reports_a_descriptor_without_config_gen()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(withConfigGen: false);

        var result = await _dispatcher.ConfigApplyAsync("agent-1", "srv-1", ct);

        result.Outcome.Should().Be(ServerDispatchOutcome.NotConfigured);
    }
}
