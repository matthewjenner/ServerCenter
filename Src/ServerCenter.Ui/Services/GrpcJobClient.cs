using System.Runtime.CompilerServices;
using Grpc.Core;
using ServerCenter.Contracts.V1;

namespace ServerCenter.Ui.Services;

// Streams recent jobs from JobView.WatchJobs and triggers a service restart via RestartService.
public sealed class GrpcJobClient(string address) : IJobClient
{
    public async IAsyncEnumerable<JobListSnapshot> Watch([EnumeratorCancellation] CancellationToken ct)
    {
        using var channel = GrpcChannels.Create(address);
        var client = new JobView.JobViewClient(channel);
        using var call = client.WatchJobs(new WatchJobsRequest(), cancellationToken: ct);

        while (await call.ResponseStream.MoveNext(ct))
        {
            yield return call.ResponseStream.Current;
        }
    }

    public async Task<string> RestartServiceAsync(string agentId, string unit, CancellationToken ct)
    {
        using var channel = GrpcChannels.Create(address);
        var client = new JobView.JobViewClient(channel);
        var response = await client.RestartServiceAsync(
            new RestartServiceRequest { AgentId = agentId, Unit = unit }, cancellationToken: ct);
        return response.JobId;
    }

    public async Task<UpdateTriggerResult> TriggerUpdateAsync(
        string agentId, string policyId, string? serviceUnit, CancellationToken ct)
    {
        using var channel = GrpcChannels.Create(address);
        var client = new JobView.JobViewClient(channel);
        var request = new TriggerUpdateRequest { AgentId = agentId, PolicyId = policyId };
        if (!string.IsNullOrWhiteSpace(serviceUnit))
        {
            request.ServiceUnit = serviceUnit;
        }

        var response = await client.TriggerUpdateAsync(request, cancellationToken: ct);
        return new UpdateTriggerResult(
            response.Outcome, string.IsNullOrEmpty(response.JobId) ? null : response.JobId, response.Reason);
    }
}
