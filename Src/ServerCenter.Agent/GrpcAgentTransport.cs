using System.Runtime.CompilerServices;
using Grpc.Core;
using Grpc.Net.Client;
using ServerCenter.Contracts.V1;
using ServerCenter.Core.Transport;

namespace ServerCenter.Agent;

// Adapts a gRPC bidi call to IAgentTransport. Owns the channel and completes the request
// stream on dispose. Writes are serialized (gRPC forbids concurrent writes on a stream) so
// that once Phase 3 has the agent emitting job progress alongside heartbeats they cannot race.
public sealed class GrpcAgentTransport(GrpcChannel channel, AsyncDuplexStreamingCall<AgentMessage, ControllerMessage> call)
    : IAgentTransport, IAsyncDisposable
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async IAsyncEnumerable<ControllerMessage> Incoming([EnumeratorCancellation] CancellationToken ct)
    {
        while (await call.ResponseStream.MoveNext(ct))
        {
            yield return call.ResponseStream.Current;
        }
    }

    public async ValueTask SendAsync(AgentMessage message, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await call.RequestStream.WriteAsync(message, ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await call.RequestStream.CompleteAsync();
        }
        catch
        {
            // stream already torn down; nothing to complete
        }

        call.Dispose();
        channel.Dispose();
        _writeLock.Dispose();
    }
}
