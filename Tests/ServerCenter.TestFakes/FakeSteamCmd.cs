using ServerCenter.Core.Jobs;
using ServerCenter.Core.Primitives;

namespace ServerCenter.TestFakes;

// An in-memory ISteamCmd: records the app requests and returns a scripted result, so the capability
// layer runs with no real Steam download.
public sealed class FakeSteamCmd : ISteamCmd
{
    public List<SteamAppRequest> Requests { get; } = [];

    public SteamAppResult Result { get; set; } = new(Success: true, BuildId: "12345", FailReason: null);

    public string? InstalledBuildId { get; set; } = "12345";

    public Task<SteamAppResult> EnsureAppAsync(SteamAppRequest request, IJobSink sink, CancellationToken ct)
    {
        Requests.Add(request);
        sink.Log(LogStream.Note, $"fake steamcmd app_update {request.AppId}");
        return Task.FromResult(Result);
    }

    public Task<string?> GetInstalledBuildIdAsync(string installDir, long appId, CancellationToken ct) =>
        Task.FromResult(InstalledBuildId);
}
