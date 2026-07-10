using ServerCenter.Contracts.V1;
using ServerCenter.Controller.Persistence;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Transport;

namespace ServerCenter.Controller.Services;

// Creates a persisted job (at Queued) and pushes its Command to the target agent if connected.
// If the agent is offline the job stays Queued (delivery-on-reconnect is a later enhancement).
// Every mutation is a job (brief 3.6). Node id == agent id in the current 1:1 mapping.
public sealed class JobDispatcher(JobRepository jobs, ConnectedAgents agents, TimeProvider clock)
{
    public async Task<string> DispatchAsync(
        string agentId, string type, string paramsJson, bool cancellable, bool requeueable, CancellationToken ct)
    {
        var jobId = Guid.NewGuid().ToString("N");
        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();

        await jobs.InsertAsync(new Job
        {
            Id = jobId,
            NodeId = agentId,
            Type = type,
            ParamsJson = paramsJson,
            State = Core.Jobs.JobState.Queued,
            Cancellable = cancellable,
            Requeueable = requeueable,
            CreatedAtUnixMs = now
        }, ct);

        if (agents.TryGet(agentId, out var stream) && stream is not null)
        {
            await stream.SendAsync(
                new ControllerMessage
                {
                    Envelope = Envelopes.New(),
                    Command = new Command { JobId = jobId, Type = type, ParamsJson = paramsJson, Cancellable = cancellable }
                },
                ct);
        }

        return jobId;
    }
}
