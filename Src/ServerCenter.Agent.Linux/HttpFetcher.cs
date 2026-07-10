namespace ServerCenter.Agent.Linux;

// Real HTTP fetching over a shared HttpClient (used on Linux nodes). Anonymous by default, matching
// the SteamCMD-anonymous ethos; a PlexPass token, if ever needed for beta channels, is a later
// addition passed as a header.
public sealed class HttpFetcher(HttpClient client) : IHttpFetcher
{
    public Task<string> GetStringAsync(string url, CancellationToken ct) => client.GetStringAsync(url, ct);

    public async Task<string> DownloadToFileAsync(string url, string destinationPath, CancellationToken ct)
    {
        await using Stream response = await client.GetStreamAsync(url, ct);
        await using FileStream file = File.Create(destinationPath);
        await response.CopyToAsync(file, ct);
        return destinationPath;
    }
}
