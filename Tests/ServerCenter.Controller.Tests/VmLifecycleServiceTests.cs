using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Primitives;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Controller.Tests;

// Controller-driven VM lifecycle: the action runs against local libvirt (not an agent) and persists
// as a real job. Resolves the node's linked domain; fails cleanly for missing node / no domain.
public sealed class VmLifecycleServiceTests : IAsyncLifetime
{
    private TempDatabase _db = null!;
    private JobRepository _jobs = null!;
    private AgentNodeRepository _nodes = null!;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        _db = await TempDatabase.CreateAsync(ct);
        _jobs = new JobRepository(_db.Database);
        _nodes = new AgentNodeRepository(_db.Database);
        await _nodes.EnsureAgentAsync("a1", "a1", "fpr", 1, ct);
        await _nodes.EnsureNodeAsync("cs2-node", "a1", "guest", "managed", 1, ct);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    private VmLifecycleService Service(ILibvirtHost libvirt) =>
        new(libvirt, _jobs, _nodes, new FakeTimeProvider(), NullLogger<VmLifecycleService>.Instance);

    [Fact]
    public async Task Start_runs_libvirt_and_persists_a_succeeded_vm_start_job()
    {
        var ct = TestContext.Current.CancellationToken;
        await _nodes.SetLibvirtDomainAsync("cs2-node", "cs2-ffa", ct);
        var libvirt = new FakeLibvirtHost();
        libvirt.Seed("cs2-ffa", "uuid-1", DomainState.ShutOff);

        var result = await Service(libvirt).DispatchAsync("cs2-node", VmAction.Start, ct);

        result.Outcome.Should().Be(VmDispatchOutcome.Dispatched);
        var job = await _jobs.GetAsync(result.JobId!, ct);
        job!.Type.Should().Be("vm.start");
        job.State.Should().Be(Core.Jobs.JobState.Succeeded);
        libvirt.Calls.Should().ContainSingle().Which.Should().Be(("start", "cs2-ffa"));
    }

    [Fact]
    public async Task A_missing_node_is_not_found()
    {
        var result = await Service(new FakeLibvirtHost())
            .DispatchAsync("nope", VmAction.Start, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(VmDispatchOutcome.NotFound);
    }

    [Fact]
    public async Task A_node_with_no_linked_domain_reports_no_domain()
    {
        var result = await Service(new FakeLibvirtHost())
            .DispatchAsync("cs2-node", VmAction.Start, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(VmDispatchOutcome.NoDomain);
    }

    [Fact]
    public async Task A_libvirt_failure_persists_a_failed_job()
    {
        var ct = TestContext.Current.CancellationToken;
        await _nodes.SetLibvirtDomainAsync("cs2-node", "cs2-ffa", ct);

        // NullLibvirtHost throws on lifecycle actions.
        var result = await Service(new NullLibvirtHost()).DispatchAsync("cs2-node", VmAction.Stop, ct);

        result.Outcome.Should().Be(VmDispatchOutcome.Dispatched);
        (await _jobs.GetAsync(result.JobId!, ct))!.State.Should().Be(Core.Jobs.JobState.Failed);
    }
}
