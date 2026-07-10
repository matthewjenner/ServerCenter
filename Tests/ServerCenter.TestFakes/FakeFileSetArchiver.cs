using ServerCenter.Core.Capabilities;

namespace ServerCenter.TestFakes;

// Records the paths/excludes it was asked to archive and returns a canned archive stream; notes when
// an extract happened. Lets SaveBackup's quiesce/key logic run with no real filesystem.
public sealed class FakeFileSetArchiver : IFileSetArchiver
{
    public (IReadOnlyList<string> Paths, IReadOnlyList<string> Exclude)? LastArchive { get; private set; }

    public byte[] ArchiveBytes { get; set; } = [1, 2, 3];

    public bool Extracted { get; private set; }

    public Task<Stream> CreateArchiveAsync(
        IReadOnlyList<string> paths, IReadOnlyList<string> exclude, CancellationToken ct)
    {
        LastArchive = (paths, exclude);
        return Task.FromResult<Stream>(new MemoryStream(ArchiveBytes));
    }

    public Task ExtractAsync(Stream archive, CancellationToken ct)
    {
        Extracted = true;
        return Task.CompletedTask;
    }
}
