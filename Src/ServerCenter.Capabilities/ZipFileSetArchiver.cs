using System.IO.Compression;
using System.IO.Enumeration;
using ServerCenter.Core.Capabilities;

namespace ServerCenter.Capabilities;

// The real file-set archiver: zips the declared save paths (skipping exclude globs) into a stream,
// and restores entries to their original absolute paths. Entry names store the POSIX absolute path
// without the leading slash; restore re-roots at "/". Real filesystem I/O - smoked at Tier 2, not
// unit tested (the SaveBackup capability's logic is tested against the fake archiver).
public sealed class ZipFileSetArchiver : IFileSetArchiver
{
    public Task<Stream> CreateArchiveAsync(
        IReadOnlyList<string> paths, IReadOnlyList<string> exclude, CancellationToken ct)
    {
        MemoryStream buffer = new MemoryStream();
        using (ZipArchive zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string path in paths)
            {
                if (File.Exists(path))
                {
                    AddFile(zip, path, exclude);
                }
                else if (Directory.Exists(path))
                {
                    foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        AddFile(zip, file, exclude);
                    }
                }
            }
        }

        buffer.Position = 0;
        return Task.FromResult<Stream>(buffer);
    }

    public Task ExtractAsync(Stream archive, CancellationToken ct)
    {
        using ZipArchive zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string target = "/" + entry.FullName;
            string? directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            entry.ExtractToFile(target, overwrite: true);
        }

        return Task.CompletedTask;
    }

    private static void AddFile(ZipArchive zip, string file, IReadOnlyList<string> exclude)
    {
        string name = Path.GetFileName(file);
        foreach (string glob in exclude)
        {
            if (FileSystemName.MatchesSimpleExpression(glob, name))
            {
                return; // excluded
            }
        }

        zip.CreateEntryFromFile(file, file.TrimStart('/'));
    }
}
