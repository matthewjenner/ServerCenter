using AwesomeAssertions;
using ServerCenter.Capabilities;
using ServerCenter.Core.Capabilities;
using ServerCenter.Core.Games;
using ServerCenter.Core.Primitives;
using ServerCenter.Primitives.Rcon;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Capabilities.Tests;

// Save-backup: optional RCON quiesce first, archive the declared paths, store at the instance's own
// versioned key; restore fetches a version and extracts.
public sealed class SaveBackupCapabilityTests
{
    private static readonly Dictionary<string, string> Params = new()
    {
        ["ports.rcon"] = "27016",
        ["rcon.password"] = "secret"
    };

    [Fact]
    public async Task Backup_quiesces_then_archives_and_puts_to_the_instance_key()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        SaveBackupSpec spec = new SaveBackupSpec("file-set", ["/opt/cs2/saves"], ["*.tmp"], new QuiesceSpec("rcon", "cvarlist_flush"));
        FakeRconChannelFactory rcon = new FakeRconChannelFactory("secret");
        FakeFileSetArchiver archiver = new FakeFileSetArchiver();
        FakeObjectStore store = new FakeObjectStore();

        await new SaveBackupCapability(spec, archiver, store, new SourceRconClient(rcon))
            .BackupAsync(new SaveBackupContext("srv-1", Params), new RecordingJobSink(), ct);

        rcon.Last!.Sent.Should().Contain(p =>
            p.Type == RconPacketTypes.ExecCommand && p.Body == "cvarlist_flush");
        archiver.LastArchive!.Value.Paths.Should().Equal("/opt/cs2/saves");
        archiver.LastArchive.Value.Exclude.Should().Equal("*.tmp");
        store.PutKeys.Should().Equal("saves/srv-1/saves.zip");
    }

    [Fact]
    public async Task Backup_without_quiesce_never_touches_rcon()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        SaveBackupSpec spec = new SaveBackupSpec("file-set", ["/opt/cs2/saves"], []);
        FakeRconChannelFactory rcon = new FakeRconChannelFactory("secret");
        FakeObjectStore store = new FakeObjectStore();

        await new SaveBackupCapability(spec, new FakeFileSetArchiver(), store, new SourceRconClient(rcon))
            .BackupAsync(new SaveBackupContext("srv-1", Params), new RecordingJobSink(), ct);

        rcon.Last.Should().BeNull(); // no RCON connection was opened
        store.PutKeys.Should().Equal("saves/srv-1/saves.zip");
    }

    [Fact]
    public async Task Restore_fetches_the_snapshot_version_and_extracts()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        SaveBackupSpec spec = new SaveBackupSpec("file-set", ["/opt/cs2/saves"], []);
        FakeFileSetArchiver archiver = new FakeFileSetArchiver();
        FakeObjectStore store = new FakeObjectStore();
        SaveBackupCapability capability = new SaveBackupCapability(spec, archiver, store, new SourceRconClient(new FakeRconChannelFactory("secret")));

        await capability.BackupAsync(new SaveBackupContext("srv-1", Params), new RecordingJobSink(), ct);
        await capability.RestoreAsync(new SaveRestoreContext("srv-1", "v1"), new RecordingJobSink(), ct);

        archiver.Extracted.Should().BeTrue();
    }
}
