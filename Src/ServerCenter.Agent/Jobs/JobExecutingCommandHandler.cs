using Microsoft.Extensions.Logging;
using ServerCenter.Contracts.V1;
using ServerCenter.Core.Connection;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Transport;

namespace ServerCenter.Agent.Jobs;

// Dispatches a pushed Command to the executor for its job type, runs it in the background (so the
// read loop is not blocked), streams progress via a TransportJobSink, tracks local state for
// resync, and sends a terminal CommandResult. Cancellation lands in a later slice.
public sealed class JobExecutingCommandHandler : IAgentCommandHandler
{
    private readonly IReadOnlyDictionary<string, IJobExecutor> _byType;
    private readonly AgentJobStore _store;
    private readonly ILogger<JobExecutingCommandHandler> _logger;

    public JobExecutingCommandHandler(
        IEnumerable<IJobExecutor> executors, AgentJobStore store, ILogger<JobExecutingCommandHandler> logger)
    {
        _byType = executors.ToDictionary(e => e.JobType);
        _store = store;
        _logger = logger;
    }

    public Task OnCommandAsync(Command command, IAgentTransport transport, CancellationToken ct)
    {
        // Run in the background so the session read loop keeps pumping.
        _ = RunAsync(command, transport, ct);
        return Task.CompletedTask;
    }

    public Task OnCancelAsync(CancelJob cancel, CancellationToken ct) => Task.CompletedTask;

    private async Task RunAsync(Command command, IAgentTransport transport, CancellationToken ct)
    {
        _store.MarkRunning(command.JobId);
        var sink = new TransportJobSink(transport, command.JobId, ct);

        JobOutcome outcome;
        try
        {
            if (!_byType.TryGetValue(command.Type, out var executor))
            {
                outcome = JobOutcome.Failure($"no executor for job type '{command.Type}'");
            }
            else
            {
                outcome = await executor.ExecuteAsync(
                    new JobContext(command.JobId, command.Type, command.ParamsJson), sink, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return; // shutting down; the controller will resync
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} ({Type}) threw", command.JobId, command.Type);
            outcome = JobOutcome.Failure(ex.Message);
        }

        _store.MarkFinished(command.JobId, outcome.Succeeded, sink.LastSeq);

        await transport.SendAsync(
            new AgentMessage
            {
                Envelope = Envelopes.New(),
                CommandResult = new CommandResult
                {
                    JobId = command.JobId,
                    FinalState = outcome.Succeeded ? Contracts.V1.JobState.Succeeded : Contracts.V1.JobState.Failed,
                    FailReason = outcome.FailReason ?? string.Empty
                }
            },
            ct);
    }
}
