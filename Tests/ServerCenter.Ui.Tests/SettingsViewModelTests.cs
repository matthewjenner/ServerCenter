using System.Text.Json;
using AwesomeAssertions;
using ServerCenter.Ui.Services;
using ServerCenter.Ui.ViewModels;
using Xunit;

namespace ServerCenter.Ui.Tests;

// The structured update-policy editor: fields serialize to the canonical dialect (camelCase props,
// kebab enum tokens, omitted-when-irrelevant), clicking a defined policy parses it back into the
// fields, and Store auto-bumps the version to a new revision.
public sealed class SettingsViewModelTests
{
    [Fact]
    public void Preview_serializes_the_fields_into_the_canonical_dialect()
    {
        SettingsViewModel vm = new SettingsViewModel
        {
            PolicyId = "plex",
            PolicyProvider = "plex",
            PolicyHow = "stop-update-start",
            PolicyServiceUnit = "plexmediaserver.service",
            PolicyReboot = "never",
            PolicyApproval = "auto",
            PreflightNotify = true
        };

        JsonElement root = Parse(vm.PolicyPreview);
        root.GetProperty("id").GetString().Should().Be("plex");
        root.GetProperty("what").GetProperty("provider").GetString().Should().Be("plex");
        root.GetProperty("how").GetString().Should().Be("stop-update-start");
        root.GetProperty("serviceUnit").GetString().Should().Be("plexmediaserver.service");
        root.GetProperty("reboot").GetString().Should().Be("never");
        root.GetProperty("preflight").EnumerateArray().Select(e => e.GetString()).Should().Equal("notify");
        root.GetProperty("version").GetInt32().Should().Be(1);   // new id -> v1
    }

    [Fact]
    public void Service_unit_is_omitted_when_how_does_not_bracket_a_service()
    {
        SettingsViewModel vm = new SettingsViewModel
        {
            PolicyId = "apt",
            PolicyHow = "in-place",
            PolicyServiceUnit = "should-be-ignored.service"
        };

        Parse(vm.PolicyPreview).TryGetProperty("serviceUnit", out _).Should().BeFalse();
    }

    [Fact]
    public void Window_fields_appear_only_when_scheduled()
    {
        SettingsViewModel vm = new SettingsViewModel { PolicyId = "nightly" };

        vm.PolicyCron = "0 3 * * *";
        vm.PolicyWindowMinutes = 30;
        JsonElement manual = Parse(vm.PolicyPreview).GetProperty("when");
        manual.TryGetProperty("cron", out _).Should().BeFalse();          // still manual: cron omitted

        vm.PolicyWhenMode = "window";
        JsonElement windowed = Parse(vm.PolicyPreview).GetProperty("when");
        windowed.GetProperty("mode").GetString().Should().Be("window");
        windowed.GetProperty("cron").GetString().Should().Be("0 3 * * *");
        windowed.GetProperty("windowMinutes").GetInt32().Should().Be(30);
    }

    [Fact]
    public void NeedsServiceUnit_and_IsWindowed_track_the_dropdowns()
    {
        SettingsViewModel vm = new SettingsViewModel();

        vm.NeedsServiceUnit.Should().BeFalse();
        vm.PolicyHow = "drain-then-update";
        vm.NeedsServiceUnit.Should().BeTrue();

        vm.IsWindowed.Should().BeFalse();
        vm.PolicyWhenMode = "window";
        vm.IsWindowed.Should().BeTrue();
    }

    [Fact]
    public void Loading_a_defined_policy_parses_it_into_the_fields()
    {
        FakeAdminClient client = new FakeAdminClient
        {
            Docs = [new PolicyDoc("plex",
                "{\"id\":\"plex\",\"version\":2,\"what\":{\"provider\":\"plex\"},\"how\":\"stop-update-start\"," +
                "\"serviceUnit\":\"plexmediaserver.service\",\"when\":{\"mode\":\"manual\"},\"reboot\":\"never\"," +
                "\"preflight\":[\"notify\",\"quiesce\"],\"approval\":\"auto\"}")]
        };
        SettingsViewModel vm = new SettingsViewModel();
        vm.UseClient(client);   // synchronous fake populates the policy list inline

        vm.LoadPolicyCommand.Execute("plex");

        vm.PolicyId.Should().Be("plex");
        vm.PolicyProvider.Should().Be("plex");
        vm.PolicyHow.Should().Be("stop-update-start");
        vm.PolicyServiceUnit.Should().Be("plexmediaserver.service");
        vm.PolicyReboot.Should().Be("never");
        vm.PreflightNotify.Should().BeTrue();
        vm.PreflightQuiesce.Should().BeTrue();
        vm.PreflightSnapshotFirst.Should().BeFalse();
    }

    [Fact]
    public async Task Store_bumps_the_version_of_an_existing_policy_and_posts_it()
    {
        FakeAdminClient client = new FakeAdminClient
        {
            Docs = [new PolicyDoc("apt", "{\"id\":\"apt\",\"version\":1,\"what\":{\"provider\":\"apt\"},\"how\":\"in-place\"," +
                "\"when\":{\"mode\":\"manual\"},\"reboot\":\"if-required\",\"preflight\":[\"notify\"],\"approval\":\"auto\"}")]
        };
        SettingsViewModel vm = new SettingsViewModel();
        vm.UseClient(client);
        vm.LoadPolicyCommand.Execute("apt");   // load apt v1

        await vm.StorePolicyCommand.ExecuteAsync(null);

        client.LastStore!.Value.Surface.Should().Be("update-policies");
        Parse(client.LastStore!.Value.Body).GetProperty("version").GetInt32().Should().Be(2);   // v1 -> v2
        vm.PolicyStatus.Should().Contain("v2");
    }

    [Fact]
    public async Task Store_requires_an_id()
    {
        FakeAdminClient client = new FakeAdminClient();
        SettingsViewModel vm = new SettingsViewModel();
        vm.UseClient(client);   // PolicyId is empty

        await vm.StorePolicyCommand.ExecuteAsync(null);

        client.LastStore.Should().BeNull();
        vm.PolicyStatus.Should().Contain("id");
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class FakeAdminClient : IAdminClient
    {
        public IReadOnlyList<PolicyDoc> Docs { get; set; } = [];
        public (string Surface, string Body)? LastStore { get; private set; }

        public Task<string> StoreAsync(string surface, string bodyJson, CancellationToken ct)
        {
            LastStore = (surface, bodyJson);
            return Task.FromResult("{\"id\":\"x\",\"version\":1}");
        }

        public Task<IReadOnlyList<PolicyDoc>> ListPoliciesAsync(CancellationToken ct) => Task.FromResult(Docs);

        public Task<string> LinkDomainAsync(string nodeId, string domain, CancellationToken ct) => Task.FromResult(string.Empty);

        public Task<string> ServerJobAsync(string kind, string agentId, string instanceId, CancellationToken ct) => Task.FromResult(string.Empty);

        public Task<IReadOnlyList<ServerInstanceRow>> ListServerInstancesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ServerInstanceRow>>([]);

        public Task<IReadOnlyList<string>> ListServicesAsync(string nodeId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> ListLibvirtDomainsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> ListPolicyIdsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<EnrollmentTokenResult> MintEnrollmentTokenAsync(string displayName, int ttlMinutes, CancellationToken ct) =>
            Task.FromResult(new EnrollmentTokenResult("tok", displayName, 0));

        public Task<IReadOnlyList<GameOption>> ListGamesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<GameOption>>([]);

        public Task<string> RemoveServerInstanceAsync(string instanceId, CancellationToken ct) => Task.FromResult(string.Empty);

        public Task<IReadOnlyList<string>> ListConfigFilesAsync(string instanceId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string> DispatchConfigReadAsync(string instanceId, string path, CancellationToken ct) => Task.FromResult(string.Empty);

        public Task<string> DispatchConfigWriteAsync(string instanceId, string path, string content, CancellationToken ct) =>
            Task.FromResult(string.Empty);

        public Task<IReadOnlyList<JobLogEntry>> GetJobLogsAsync(string jobId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<JobLogEntry>>([]);
    }
}
