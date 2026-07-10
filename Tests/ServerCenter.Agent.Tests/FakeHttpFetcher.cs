using ServerCenter.Agent.Linux;

namespace ServerCenter.Agent.Tests;

// Canned HTTP: GetString returns a fixed body (e.g. the Plex manifest); DownloadToFile records the
// requested url/destination and creates no real file. Lets the Plex provider's parse/compare/select
// logic run without network I/O.
internal sealed class FakeHttpFetcher : IHttpFetcher
{
    public string Body { get; set; } = string.Empty;

    public List<string> GetUrls { get; } = [];

    public List<(string Url, string Destination)> Downloads { get; } = [];

    public Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        GetUrls.Add(url);
        return Task.FromResult(Body);
    }

    public Task<string> DownloadToFileAsync(string url, string destinationPath, CancellationToken ct)
    {
        Downloads.Add((url, destinationPath));
        return Task.FromResult(destinationPath);
    }
}
