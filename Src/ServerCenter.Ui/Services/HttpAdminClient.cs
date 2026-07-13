using System.Net;
using System.Net.Security;
using System.Text;
using System.Text.Json;

namespace ServerCenter.Ui.Services;

// REST operator client. Over http the controller speaks h2c (HTTP/2 cleartext), so requests are
// pinned to HTTP/2 prior-knowledge; over https the server cert is trust-on-first-use for now (matches
// GrpcChannels). One instance per connected address; recreated on reconnect.
public sealed class HttpAdminClient : IAdminClient
{
    private readonly HttpClient _http;

    public HttpAdminClient(string address)
    {
        bool https = address.StartsWith("https", StringComparison.OrdinalIgnoreCase);
        SocketsHttpHandler handler = new SocketsHttpHandler();
        if (https)
        {
            handler.SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            };
        }
        else
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        }

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(address),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
    }

    public Task<string> LinkDomainAsync(string nodeId, string domain, CancellationToken ct) =>
        PostAsync($"/nodes/{Uri.EscapeDataString(nodeId)}/libvirt-domain", $"{{\"domain\":\"{domain}\"}}", ct);

    public Task<string> StoreAsync(string surface, string bodyJson, CancellationToken ct) =>
        PostAsync($"/{surface}", bodyJson, ct);

    public Task<string> ServerJobAsync(string kind, string agentId, string instanceId, CancellationToken ct) =>
        PostAsync($"/jobs/{kind}", $"{{\"agentId\":\"{agentId}\",\"instanceId\":\"{instanceId}\"}}", ct);

    public async Task<IReadOnlyList<ServerInstanceRow>> ListServerInstancesAsync(CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.GetAsync("/server-instances", ct);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
        List<ServerInstanceRow>? rows = await JsonSerializer.DeserializeAsync<List<ServerInstanceRow>>(
            stream, JsonSerializerOptions.Web, ct);
        return rows ?? [];
    }

    public Task<IReadOnlyList<string>> ListServicesAsync(string nodeId, CancellationToken ct) =>
        GetStringListAsync($"/nodes/{Uri.EscapeDataString(nodeId)}/services", ct);

    public Task<IReadOnlyList<string>> ListLibvirtDomainsAsync(CancellationToken ct) =>
        GetStringListAsync("/libvirt-domains", ct);

    public async Task<IReadOnlyList<string>> ListPolicyIdsAsync(CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.GetAsync("/update-policies", ct);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
        List<PolicyRef>? policies = await JsonSerializer.DeserializeAsync<List<PolicyRef>>(stream, JsonSerializerOptions.Web, ct);
        return policies?.Select(p => p.Id).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList() ?? [];
    }

    public async Task<IReadOnlyList<PolicyDoc>> ListPoliciesAsync(CancellationToken ct)
    {
        // GET /update-policies returns the full policy objects; keep each one's raw JSON so the editor
        // can load it verbatim, and read its id for the label.
        using HttpResponseMessage response = await _http.GetAsync("/update-policies", ct);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
        using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        List<PolicyDoc> docs = new List<PolicyDoc>();
        foreach (JsonElement element in doc.RootElement.EnumerateArray())
        {
            string id = element.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
            if (!string.IsNullOrEmpty(id))
            {
                docs.Add(new PolicyDoc(id, element.GetRawText()));
            }
        }

        return docs;
    }

    public async Task<IReadOnlyList<GameOption>> ListGamesAsync(CancellationToken ct)
    {
        Dictionary<string, int> descriptors = await IdVersionsAsync("/game-descriptors", ct);
        Dictionary<string, int> recipes = await IdVersionsAsync("/build-recipes", ct);
        List<GameOption> games = new List<GameOption>();
        foreach (KeyValuePair<string, int> descriptor in descriptors)
        {
            int? recipeVersion = recipes.TryGetValue(descriptor.Key, out int rv) ? rv : null;
            games.Add(new GameOption(descriptor.Key, descriptor.Value, recipeVersion));
        }

        return games;
    }

    // The latest (id -> version) from a /game-descriptors or /build-recipes list (one row per id).
    private async Task<Dictionary<string, int>> IdVersionsAsync(string path, CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
        using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        Dictionary<string, int> map = new Dictionary<string, int>();
        foreach (JsonElement element in doc.RootElement.EnumerateArray())
        {
            string id = element.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
            int version = element.TryGetProperty("version", out JsonElement verEl) ? verEl.GetInt32() : 0;
            if (!string.IsNullOrEmpty(id))
            {
                map[id] = version;
            }
        }

        return map;
    }

    public async Task<string> RemoveServerInstanceAsync(string instanceId, CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.DeleteAsync($"/server-instances/{Uri.EscapeDataString(instanceId)}", ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{(int)response.StatusCode}: {body}");
        }

        return body;
    }

    public async Task<EnrollmentTokenResult> MintEnrollmentTokenAsync(string displayName, int ttlMinutes, CancellationToken ct)
    {
        string requestJson = JsonSerializer.Serialize(new { displayName, ttlMinutes });
        using StringContent content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _http.PostAsync("/enroll-token", content, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{(int)response.StatusCode}: {body}");
        }

        MintedToken? minted = JsonSerializer.Deserialize<MintedToken>(body, JsonSerializerOptions.Web);
        return minted is null
            ? throw new HttpRequestException("empty token response")
            : new EnrollmentTokenResult(minted.Token, minted.DisplayName, minted.ExpiresAtUnixMs);
    }

    private sealed record MintedToken(string Token, string DisplayName, long ExpiresAtUnixMs, int TtlMinutes);

    private async Task<IReadOnlyList<string>> GetStringListAsync(string path, CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
        List<string>? items = await JsonSerializer.DeserializeAsync<List<string>>(stream, JsonSerializerOptions.Web, ct);
        return items ?? [];
    }

    private sealed record PolicyRef(string Id);

    private async Task<string> PostAsync(string path, string json, CancellationToken ct)
    {
        using StringContent content = new StringContent(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _http.PostAsync(path, content, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{(int)response.StatusCode}: {body}");
        }

        return body;
    }
}
