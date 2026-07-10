using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using ServerCenter.Contracts.V1;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Controller.Tests;

public sealed class JobDispatcherTests : IAsyncLifetime
{
    private TempDatabase _db = null!;
    private JobRepository _jobs = null!;
    private ConnectedAgents _agents = null!;
    private JobDispatcher _dispatcher = null!;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        _db = await TempDatabase.CreateAsync(ct);
        _jobs = new JobRepository(_db.Database);
        _agents = new ConnectedAgents();
        _dispatcher = new JobDispatcher(_jobs, _agents, new FakeTimeProvider());

        var repo = new AgentNodeRepository(_db.Database);
        await repo.EnsureAgentAsync("a1", "a1", "fpr", 1, ct);
        await repo.EnsureNodeAsync("a1", "a1", "guest", "managed", 1, ct);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Dispatch_persists_queued_and_pushes_the_command_to_a_connected_agent()
    {
        var ct = TestContext.Current.CancellationToken;
        using var link = new InMemoryDuplexLink();
        _agents.Register("a1", link.ControllerSide);

        var jobId = await _dispatcher.DispatchAsync("a1", "service.restart", "{\"unit\":\"web\"}", false, false, ct);

        (await _jobs.GetAsync(jobId, ct))!.State.Should().Be(ServerCenter.Core.Jobs.JobState.Queued);

        await using var incoming = link.AgentSide.Incoming(ct).GetAsyncEnumerator(ct);
        (await incoming.MoveNextAsync()).Should().BeTrue();
        incoming.Current.PayloadCase.Should().Be(ControllerMessage.PayloadOneofCase.Command);
        incoming.Current.Command.JobId.Should().Be(jobId);
        incoming.Current.Command.Type.Should().Be("service.restart");
    }

    [Fact]
    public async Task Dispatch_to_an_offline_agent_still_persists_the_queued_job()
    {
        var ct = TestContext.Current.CancellationToken;

        // "a1" is not registered as connected.
        var jobId = await _dispatcher.DispatchAsync("a1", "service.restart", "{}", false, false, ct);

        (await _jobs.GetAsync(jobId, ct))!.State.Should().Be(ServerCenter.Core.Jobs.JobState.Queued);
    }
}
