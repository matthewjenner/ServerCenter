using ServerCenter.Core.Primitives;

namespace ServerCenter.Primitives.Libvirt;

// Pure parsing of virsh text output into the ILibvirtHost model. Kept separate from the process
// wrapper so the fiddly bit (column/field parsing, state mapping) is directly unit-testable without
// a real libvirt - which is invisible to containers, so the real VirshLibvirtHost is Tier 3 only.
public static class VirshOutputParser
{
    // Maps virsh's state text to DomainState. virsh reports e.g. "running", "paused", "shut off",
    // "in shutdown", "crashed".
    public static DomainState ParseState(string text) => text.Trim().ToLowerInvariant() switch
    {
        "running" => DomainState.Running,
        "idle" => DomainState.Running,
        "paused" => DomainState.Paused,
        "pmsuspended" => DomainState.Paused,
        "in shutdown" => DomainState.Shutdown,
        "shutdown" => DomainState.Shutdown,
        "shut off" => DomainState.ShutOff,
        "crashed" => DomainState.Crashed,
        "no state" => DomainState.NoState,
        _ => DomainState.Unknown
    };

    // Parses `virsh list --all`, whose table is:
    //    Id   Name       State
    //   ----------------------------
    //    1    cs2-ffa    running
    //    -    plex-vm    shut off
    // The state column can be multiple words; UUID is not in this view (empty until GetDomain).
    public static IReadOnlyList<DomainInfo> ParseDomainList(string listOutput)
    {
        List<DomainInfo> domains = new List<DomainInfo>();
        bool pastHeader = false;
        foreach (string raw in listOutput.Split('\n'))
        {
            string line = raw.TrimEnd();
            if (!pastHeader)
            {
                if (line.Contains("----", StringComparison.Ordinal))
                {
                    pastHeader = true;
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                continue;
            }

            string name = parts[1];
            string state = string.Join(' ', parts.Skip(2));
            domains.Add(new DomainInfo(name, string.Empty, ParseState(state)));
        }

        return domains;
    }

    // Parses `virsh dominfo <domain>` key/value lines for Name, UUID, and State. Returns null if the
    // output has no Name (e.g. an unknown domain).
    public static DomainInfo? ParseDomInfo(string domInfoOutput)
    {
        string? name = null;
        string? uuid = null;
        string? state = null;

        foreach (string raw in domInfoOutput.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("Name:", StringComparison.Ordinal))
            {
                name = FieldValue(line);
            }
            else if (line.StartsWith("UUID:", StringComparison.Ordinal))
            {
                uuid = FieldValue(line);
            }
            else if (line.StartsWith("State:", StringComparison.Ordinal))
            {
                state = FieldValue(line);
            }
        }

        return name is null ? null : new DomainInfo(name, uuid ?? string.Empty, ParseState(state ?? string.Empty));
    }

    private static string FieldValue(string line) => line[(line.IndexOf(':') + 1)..].Trim();
}
