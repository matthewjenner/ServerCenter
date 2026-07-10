using System.Collections.Concurrent;
using ServerCenter.Core.Primitives;

namespace ServerCenter.Controller.Services;

// The latest known libvirt domain states, in-memory (transient live truth, like AgentPresenceStore -
// NOT persisted; the controller's precious state is the SQLite surface). Fed by LibvirtStatePoller,
// read by FleetSnapshotBuilder to derive the VM-running half of dual-truth.
public sealed class LibvirtDomainStates
{
    private readonly ConcurrentDictionary<string, DomainState> _states = new();

    public void Set(string domain, DomainState state) => _states[domain] = state;

    public bool TryGet(string domain, out DomainState state) => _states.TryGetValue(domain, out state);
}
