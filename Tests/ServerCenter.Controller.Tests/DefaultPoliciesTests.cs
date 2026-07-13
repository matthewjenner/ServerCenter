using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Updates;
using Xunit;

namespace ServerCenter.Controller.Tests;

// The seeded update policies (apt / plex / steamcmd), so the picker is never empty and the common
// profiles work with zero setup.
public sealed class DefaultPoliciesTests : IAsyncLifetime
{
    private TempDatabase _db = null!;
    private UpdatePolicyRepository _policies = null!;

    public async ValueTask InitializeAsync()
    {
        _db = await TempDatabase.CreateAsync(TestContext.Current.CancellationToken);
        _policies = new UpdatePolicyRepository(_db.Database);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Seeds_apt_plex_and_steamcmd()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        await DefaultPolicies.EnsureAsync(_policies, new FakeTimeProvider(), ct);

        UpdatePolicy plex = (await _policies.GetLatestAsync("plex", ct))!;
        plex.What.Provider.Should().Be("plex");
        plex.How.Should().Be(UpdateHow.StopUpdateStart);
        plex.ServiceUnit.Should().Be("plexmediaserver.service");   // one-click stop/start

        UpdatePolicy steamcmd = (await _policies.GetLatestAsync("steamcmd", ct))!;
        steamcmd.What.Provider.Should().Be("steamcmd");

        (await _policies.GetLatestAsync("apt", ct)).Should().NotBeNull();
    }

    [Fact]
    public async Task Ensure_is_idempotent()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        await DefaultPolicies.EnsureAsync(_policies, new FakeTimeProvider(), ct);
        await DefaultPolicies.EnsureAsync(_policies, new FakeTimeProvider(), ct);

        (await _policies.ListLatestAsync(ct)).Should().HaveCount(3);
    }
}
