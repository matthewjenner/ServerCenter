using AwesomeAssertions;
using ServerCenter.Ui.Services;
using ServerCenter.Ui.ViewModels;
using Xunit;

namespace ServerCenter.Ui.Tests;

// The raw config editor: list an instance's files, read one back off the read-job's stdout log, and
// write edits. A no-op delay keeps the read-back poll instant; the fake completes synchronously so a
// file selection loads inline.
public sealed class ConfigEditorViewModelTests
{
    [Fact]
    public async Task LoadFiles_populates_the_list_and_auto_selects_a_single_file()
    {
        FakeAdminClient client = new FakeAdminClient
        {
            Files = ["/opt/servercenter/cs2/arena1/game/csgo/cfg/gameserver.cfg"],
            ReadStdout = "hostname \"arena1\""
        };
        ConfigEditorViewModel vm = Editor(client);

        await vm.LoadFilesAsync();

        vm.Files.Should().ContainSingle();
        vm.SelectedFile.Should().Be("/opt/servercenter/cs2/arena1/game/csgo/cfg/gameserver.cfg");   // one file: opened
        vm.Content.Should().Be("hostname \"arena1\"");   // auto-load pulled the stdout line back
        vm.Loaded.Should().BeTrue();
    }

    [Fact]
    public void Selecting_a_file_reads_its_contents_back_from_the_job_log()
    {
        FakeAdminClient client = new FakeAdminClient { ReadStdout = "sv_cheats 0" };
        ConfigEditorViewModel vm = Editor(client);

        vm.SelectedFile = "server.cfg";

        client.LastRead.Should().Be(("arena1", "server.cfg"));
        vm.Content.Should().Be("sv_cheats 0");
        vm.Loaded.Should().BeTrue();
        vm.Status.Should().Contain("loaded");
    }

    [Fact]
    public void An_empty_file_reads_back_as_empty_and_still_loads()
    {
        FakeAdminClient client = new FakeAdminClient { ReadStdout = string.Empty };
        ConfigEditorViewModel vm = Editor(client);

        vm.SelectedFile = "fresh.cfg";

        vm.Content.Should().BeEmpty();
        vm.Loaded.Should().BeTrue();   // an absent/empty file is a valid load, not a timeout
    }

    [Fact]
    public async Task Save_writes_the_edited_content_back_to_the_selected_file()
    {
        FakeAdminClient client = new FakeAdminClient { ReadStdout = "old" };
        ConfigEditorViewModel vm = Editor(client);
        vm.SelectedFile = "server.cfg";
        vm.Content = "new contents";

        await vm.SaveCommand.ExecuteAsync(null);

        client.LastWrite.Should().Be(("arena1", "server.cfg", "new contents"));
        vm.Status.Should().Contain("write dispatched");
    }

    [Fact]
    public async Task Read_that_never_emits_stdout_times_out_without_marking_loaded()
    {
        FakeAdminClient client = new FakeAdminClient { ReadStdout = null };   // no stdout line ever
        ConfigEditorViewModel vm = Editor(client);

        await vm.LoadContentAsync("server.cfg");

        vm.Loaded.Should().BeFalse();
        vm.Status.Should().Contain("timed out");
    }

    [Fact]
    public async Task LoadFiles_surfaces_an_error()
    {
        ConfigEditorViewModel vm = Editor(new FakeAdminClient { Throw = true });

        await vm.LoadFilesAsync();

        vm.Files.Should().BeEmpty();
        vm.Status.Should().StartWith("error:");
    }

    private static ConfigEditorViewModel Editor(IAdminClient client) =>
        new(client, "arena1", "web-server", (_, _) => Task.CompletedTask);

    private sealed class FakeAdminClient : IAdminClient
    {
        public IReadOnlyList<string> Files { get; set; } = [];

        // The stdout line a config-read job "emits"; null = it never appears (read-back times out).
        public string? ReadStdout { get; set; } = string.Empty;
        public bool Throw { get; set; }

        public (string Instance, string Path)? LastRead { get; private set; }
        public (string Instance, string Path, string Content)? LastWrite { get; private set; }

        public Task<IReadOnlyList<string>> ListConfigFilesAsync(string instanceId, CancellationToken ct)
        {
            if (Throw)
            {
                throw new HttpRequestException("boom");
            }

            return Task.FromResult(Files);
        }

        public Task<string> DispatchConfigReadAsync(string instanceId, string path, CancellationToken ct)
        {
            LastRead = (instanceId, path);
            return Task.FromResult("readjob");
        }

        public Task<string> DispatchConfigWriteAsync(string instanceId, string path, string content, CancellationToken ct)
        {
            LastWrite = (instanceId, path, content);
            return Task.FromResult("writejob");
        }

        public Task<IReadOnlyList<JobLogEntry>> GetJobLogsAsync(string jobId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<JobLogEntry>>(
                ReadStdout is null ? [new JobLogEntry(1, "note", "read ...")] : [new JobLogEntry(1, "stdout", ReadStdout)]);

        public Task<string> LinkDomainAsync(string nodeId, string domain, CancellationToken ct) => Task.FromResult(string.Empty);

        public Task<string> StoreAsync(string surface, string bodyJson, CancellationToken ct) => Task.FromResult(string.Empty);

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

        public Task<IReadOnlyList<PolicyDoc>> ListPoliciesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PolicyDoc>>([]);

        public Task<IReadOnlyList<GameOption>> ListGamesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<GameOption>>([]);

        public Task<string> RemoveServerInstanceAsync(string instanceId, CancellationToken ct) => Task.FromResult(string.Empty);
    }
}
