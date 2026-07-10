namespace ServerCenter.Agent.Linux;

// A tiny HTTP seam so app-channel providers (Plex) are testable without real network I/O: their
// version-compare and release-selection logic runs against a fake fetcher at Tier 1, the real
// download smokes on a node (Tier 2). Kept deliberately small - just what the providers need.
public interface IHttpFetcher
{
    Task<string> GetStringAsync(string url, CancellationToken ct);

    // Streams the URL to a local path (returned) so the follow-on installer (dpkg -i) has a file.
    Task<string> DownloadToFileAsync(string url, string destinationPath, CancellationToken ct);
}
