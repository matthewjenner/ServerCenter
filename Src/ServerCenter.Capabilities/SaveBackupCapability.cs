using ServerCenter.Core.Capabilities;
using ServerCenter.Core.Games;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Primitives;

namespace ServerCenter.Capabilities;

// Save-backup via the file-set primitive: optionally quiesce the server over RCON (flush/announce),
// archive the declared save paths, and store the archive at the instance's OWN object-store key. The
// object store is versioned (S3), so each backup is a new version of a stable key and history is
// preserved without timestamped keys - a snapshot id IS an object version id (brief 3.9 / 8.4:
// game saves are data-plane, backed up by purpose, on their own path). Restore fetches a version and
// extracts it in place.
public sealed class SaveBackupCapability(
    SaveBackupSpec spec, IFileSetArchiver archiver, IObjectStore store, IRconClient rcon) : ISaveBackupCapability
{
    public CapabilityKind Kind => CapabilityKind.SaveBackup;

    public async Task BackupAsync(SaveBackupContext ctx, IJobSink sink, CancellationToken ct)
    {
        if (spec.Quiesce is { } quiesce)
        {
            sink.Log(LogStream.Note, $"quiesce ({quiesce.Via}): {quiesce.Command}");
            await using var session = await rcon.ConnectAsync(RconEndpoints.From(ctx.InstanceParams), ct);
            await session.ExecuteAsync(quiesce.Command, ct);
        }

        sink.Log(LogStream.Note, $"archiving {spec.Paths.Count} path(s)");
        await using var archive = await archiver.CreateArchiveAsync(spec.Paths, spec.Exclude, ct);

        var result = await store.PutAsync(SaveKey(ctx.InstanceId), archive, ct);
        sink.Log(LogStream.Note, $"backed up to {result.Key} (version {result.VersionId}, {result.Bytes} bytes)");
    }

    public async Task RestoreAsync(SaveRestoreContext ctx, IJobSink sink, CancellationToken ct)
    {
        var key = SaveKey(ctx.InstanceId);
        sink.Log(LogStream.Note, $"restoring {key} (version {ctx.SnapshotId})");
        await using var archive = await store.GetAsync(key, ctx.SnapshotId, ct);
        await archiver.ExtractAsync(archive, ct);
    }

    // The instance's own path: game saves are separate from the one-backup controller surface.
    private static string SaveKey(string instanceId) => $"saves/{instanceId}/saves.zip";
}
