namespace ServerCenter.Core.Capabilities;

// Archives a game's save file-set (paths minus exclude globs) into a single stream, and restores it.
// The glob enumeration + zip is real I/O behind this seam, so the SaveBackup capability's testable
// logic - quiesce ordering and the instance-scoped object-store key - runs against a fake; the real
// archiver smokes at Tier 2.
public interface IFileSetArchiver
{
    Task<Stream> CreateArchiveAsync(IReadOnlyList<string> paths, IReadOnlyList<string> exclude, CancellationToken ct);

    Task ExtractAsync(Stream archive, CancellationToken ct);
}
