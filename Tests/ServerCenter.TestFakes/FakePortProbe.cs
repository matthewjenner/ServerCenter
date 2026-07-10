using ServerCenter.Core.Primitives;

namespace ServerCenter.TestFakes;

// A scripted port probe: returns a fixed open/closed answer and records what was probed.
public sealed class FakePortProbe(bool open = true) : IPortProbe
{
    public bool Open { get; set; } = open;

    public List<(string Host, int Port)> Probes { get; } = [];

    public Task<bool> IsOpenAsync(string host, int port, CancellationToken ct)
    {
        Probes.Add((host, port));
        return Task.FromResult(Open);
    }
}
