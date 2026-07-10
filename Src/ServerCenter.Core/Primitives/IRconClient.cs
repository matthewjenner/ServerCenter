namespace ServerCenter.Core.Primitives;

// The highest-leverage primitive: powers stats, graceful shutdown, quiesce-before-backup,
// drain-before-host-reboot, admin actions across many engines. Build early and solid.
// Ships with an in-memory fake.
public interface IRconClient
{
    Task<IRconSession> ConnectAsync(RconEndpoint endpoint, CancellationToken ct);
}

public interface IRconSession : IAsyncDisposable
{
    Task<string> ExecuteAsync(string command, CancellationToken ct);
}

public sealed record RconEndpoint(string Host, int Port, string Password);
