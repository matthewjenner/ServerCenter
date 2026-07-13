using ServerCenter.Core.Capabilities;

namespace ServerCenter.Capabilities;

// Real filesystem read for the raw config editor: the current contents of a file, or null if it does
// not exist yet. The write counterpart is FileConfigWriter.
public sealed class FileConfigReader : IConfigReader
{
    public async Task<string?> ReadAsync(string path, CancellationToken ct) =>
        File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : null;
}
