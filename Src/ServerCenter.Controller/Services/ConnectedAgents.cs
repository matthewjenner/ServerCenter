using System.Collections.Concurrent;
using ServerCenter.Core.Transport;

namespace ServerCenter.Controller.Services;

// Tracks the live stream for each connected agent so the controller can push commands down.
// Registered on connect, removed on disconnect. A reconnect replaces the entry; unregister only
// removes if it is still the same stream (so a stale disconnect cannot evict a newer connection).
public sealed class ConnectedAgents
{
    private readonly ConcurrentDictionary<string, IControllerStream> _byAgent = new();

    public void Register(string agentId, IControllerStream stream) => _byAgent[agentId] = stream;

    public void Unregister(string agentId, IControllerStream stream) =>
        ((ICollection<KeyValuePair<string, IControllerStream>>)_byAgent).Remove(
            new KeyValuePair<string, IControllerStream>(agentId, stream));

    public bool TryGet(string agentId, out IControllerStream? stream) => _byAgent.TryGetValue(agentId, out stream);
}
