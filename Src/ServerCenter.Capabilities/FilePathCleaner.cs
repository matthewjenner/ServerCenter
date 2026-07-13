using ServerCenter.Core.Capabilities;

namespace ServerCenter.Capabilities;

// Real filesystem teardown: delete a file or a directory tree if present, no-op if absent. The write
// counterpart is FileConfigWriter. Absent-path is deliberately not an error so remove is idempotent
// (re-running on an already-clean box succeeds).
public sealed class FilePathCleaner : IPathCleaner
{
    public Task DeletePathAsync(string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.CompletedTask;
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }
}
