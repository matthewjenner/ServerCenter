using System.Runtime.CompilerServices;
using Grpc.Core;
using ServerCenter.Contracts.V1;
using ServerCenter.Core.Transport;

namespace ServerCenter.Controller.Grpc;

// Adapts the server-side gRPC streams to IControllerStream so the transport-agnostic handshake
// and session pump run unchanged over a real socket. Writes are serialized (gRPC forbids
// concurrent writes on a stream).
internal sealed class GrpcControllerStream(
    IAsyncStreamReader<AgentMessage> reader,
    IServerStreamWriter<ControllerMessage> writer) : IControllerStream
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async IAsyncEnumerable<AgentMessage> Incoming([EnumeratorCancellation] CancellationToken ct)
    {
        while (await reader.MoveNext(ct))
        {
            yield return reader.Current;
        }
    }

    public async ValueTask SendAsync(ControllerMessage message, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await writer.WriteAsync(message, ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
