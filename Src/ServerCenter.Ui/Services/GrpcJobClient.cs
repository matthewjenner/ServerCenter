using System.Runtime.CompilerServices;
using Grpc.Core;
using Grpc.Net.Client;
using ServerCenter.Contracts.V1;

namespace ServerCenter.Ui.Services;

// Streams recent jobs from JobView.WatchJobs and triggers a service restart via RestartService.
public sealed class GrpcJobClient(string address) : IJobClient
{
    public async IAsyncEnumerable<JobListSnapshot> Watch([EnumeratorCancellation] CancellationToken ct)
    {
        using GrpcChannel channel = GrpcChannels.Create(address);
        JobView.JobViewClient client = new JobView.JobViewClient(channel);
        using AsyncServerStreamingCall<JobListSnapshot> call = client.WatchJobs(new WatchJobsRequest(), cancellationToken: ct);

        while (await call.ResponseStream.MoveNext(ct))
        {
            yield return call.ResponseStream.Current;
        }
    }

    public async Task<string> RestartServiceAsync(string agentId, string unit, CancellationToken ct)
    {
        using GrpcChannel channel = GrpcChannels.Create(address);
        JobView.JobViewClient client = new JobView.JobViewClient(channel);
        RestartServiceResponse response = await client.RestartServiceAsync(
            new RestartServiceRequest { AgentId = agentId, Unit = unit }, cancellationToken: ct);
        return response.JobId;
    }

    public async Task<UpdateTriggerResult> TriggerUpdateAsync(
        string agentId, string policyId, string? serviceUnit, CancellationToken ct)
    {
        using GrpcChannel channel = GrpcChannels.Create(address);
        JobView.JobViewClient client = new JobView.JobViewClient(channel);
        TriggerUpdateRequest request = new TriggerUpdateRequest { AgentId = agentId, PolicyId = policyId };
        if (!string.IsNullOrWhiteSpace(serviceUnit))
        {
            request.ServiceUnit = serviceUnit;
        }

        TriggerUpdateResponse response = await client.TriggerUpdateAsync(request, cancellationToken: ct);
        return new UpdateTriggerResult(
            response.Outcome, string.IsNullOrEmpty(response.JobId) ? null : response.JobId, response.Reason);
    }

    public async Task<UpdateTriggerResult> TriggerVmActionAsync(string nodeId, string action, CancellationToken ct)
    {
        using GrpcChannel channel = GrpcChannels.Create(address);
        JobView.JobViewClient client = new JobView.JobViewClient(channel);
        TriggerVmActionResponse response = await client.TriggerVmActionAsync(
            new TriggerVmActionRequest { NodeId = nodeId, Action = MapAction(action) }, cancellationToken: ct);
        return new UpdateTriggerResult(
            response.Outcome, string.IsNullOrEmpty(response.JobId) ? null : response.JobId, response.Reason);
    }

    private static VmLifecycleAction MapAction(string action) => action.Trim().ToLowerInvariant() switch
    {
        "start" => VmLifecycleAction.Start,
        "stop" => VmLifecycleAction.Stop,
        "restart" => VmLifecycleAction.Restart,
        _ => VmLifecycleAction.Unspecified
    };
}
