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
