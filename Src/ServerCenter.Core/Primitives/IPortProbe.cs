namespace ServerCenter.Core.Primitives;

// Readiness port-probe primitive: is a game's port accepting TCP connections? This is a game-LEVEL
// signal (the actual game/query port), not process-alive - a unit can be Active while the game is
// still loading and not yet listening. Ships a fake.
public interface IPortProbe
{
    Task<bool> IsOpenAsync(string host, int port, CancellationToken ct);
}
