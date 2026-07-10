using ServerCenter.Core.Capabilities;

namespace ServerCenter.Capabilities;

// Writes rendered config to a path on the game node, creating the parent directory if needed. Real
// I/O, smoked at Tier 2.
public sealed class FileConfigWriter : IConfigWriter
{
    public async Task WriteAsync(string path, string content, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, content, ct);
    }
}
