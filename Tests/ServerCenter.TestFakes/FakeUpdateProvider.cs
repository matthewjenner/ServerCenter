using ServerCenter.Core.Jobs;
using ServerCenter.Core.Platform;

namespace ServerCenter.TestFakes;

// An in-memory IUpdateProvider: records whether/how it was asked to apply and returns a scripted
// outcome, so update.apply job flows run with no apt/dpkg/network.
public sealed class FakeUpdateProvider(string channel = "apt") : IUpdateProvider
{
    public string Channel { get; } = channel;

    public bool Applied { get; private set; }

    public UpdatePlan? LastPlan { get; private set; }

    public IReadOnlyList<AvailableUpdate> Available { get; set; } = [];

    public UpdateOutcome Outcome { get; set; } = new(Success: true, RebootRequired: false, FailReason: null);

    public Task<IReadOnlyList<AvailableUpdate>> CheckAsync(CancellationToken ct) => Task.FromResult(Available);

    public Task<UpdateOutcome> ApplyAsync(UpdatePlan plan, IJobSink sink, CancellationToken ct)
    {
        Applied = true;
        LastPlan = plan;
        sink.Log(LogStream.Note, $"fake apply on channel {Channel}");
        return Task.FromResult(Outcome);
    }

    public Task<bool> RebootRequiredAsync(CancellationToken ct) => Task.FromResult(Outcome.RebootRequired);
}
