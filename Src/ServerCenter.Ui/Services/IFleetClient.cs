using ServerCenter.Contracts.V1;

namespace ServerCenter.Ui.Services;

// The dashboard's source of fleet snapshots. Behind an interface so the view-model can be tested
// with a fake stream (no controller / gRPC).
public interface IFleetClient
{
    IAsyncEnumerable<FleetSnapshot> Watch(CancellationToken ct);
}
