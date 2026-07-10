using AwesomeAssertions;
using ServerCenter.Controller.Persistence;
using ServerCenter.Core.Updates;
using Xunit;

namespace ServerCenter.Controller.Tests;

// Policies persist as immutable versioned rows (real temp-file SQLite). A stored revision must
// round-trip through the model, and GetLatest must pick the highest version.
public sealed class UpdatePolicyRepositoryTests : IAsyncLifetime
{
    private TempDatabase _db = null!;
    private UpdatePolicyRepository _policies = null!;

    public async ValueTask InitializeAsync()
    {
        _db = await TempDatabase.CreateAsync(TestContext.Current.CancellationToken);
        _policies = new UpdatePolicyRepository(_db.Database);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    private static UpdatePolicy Policy(int version) => new()
    {
        Id = "plex-lowtraffic",
        Version = version,
        What = new UpdateWhat("plex"),
        How = UpdateHow.StopUpdateStart,
        When = new UpdateSchedule { Mode = ScheduleMode.Window, Cron = "0 4 * * *", WindowMinutes = 120 },
        Reboot = RebootPolicy.IfRequired,
        Preflight = [PreflightStep.Notify],
        Approval = ApprovalMode.Auto
    };

    [Fact]
    public async Task Insert_and_get_round_trips_the_policy()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await _policies.InsertAsync(Policy(1), 1000, ct);

        UpdatePolicy? got = await _policies.GetAsync("plex-lowtraffic", 1, ct);

        got.Should().BeEquivalentTo(Policy(1));
    }

    [Fact]
    public async Task GetLatest_returns_the_highest_version()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await _policies.InsertAsync(Policy(1), 1000, ct);
        await _policies.InsertAsync(Policy(3) with { How = UpdateHow.InPlace }, 3000, ct);
        await _policies.InsertAsync(Policy(2), 2000, ct);

        UpdatePolicy? latest = await _policies.GetLatestAsync("plex-lowtraffic", ct);

        latest!.Version.Should().Be(3);
        latest.How.Should().Be(UpdateHow.InPlace);
    }

    [Fact]
    public async Task GetLatest_is_null_for_an_unknown_id()
    {
        UpdatePolicy? latest = await _policies.GetLatestAsync("nope", TestContext.Current.CancellationToken);

        latest.Should().BeNull();
    }
}
