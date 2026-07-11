using AwesomeAssertions;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;
using Xunit;

namespace ServerCenter.Controller.Tests;

public sealed class LibvirtAutoLinkerTests
{
    [Fact]
    public void Matches_unlinked_guest_nodes_to_same_named_domains()
    {
        NodeRow[] nodes =
        [
            Node("web-server", "guest", displayName: "web-server", libvirtDomain: null),   // -> matches by id
            Node("plex", "guest", displayName: "Plex Media", libvirtDomain: null),         // -> matches by display name
            Node("host", "host", displayName: "host", libvirtDomain: null),                // host: skipped (no VM)
            Node("already", "guest", displayName: "already", libvirtDomain: "already-vm"), // already linked: skipped
            Node("lonely", "guest", displayName: "lonely", libvirtDomain: null)            // no matching domain
        ];
        string[] domains = ["web-server", "plex media", "already-vm", "unrelated"];   // case-insensitive match

        IReadOnlyList<(string NodeId, string Domain)> links = LibvirtAutoLinker.MatchNodesToDomains(nodes, domains);

        links.Should().BeEquivalentTo([("web-server", "web-server"), ("plex", "plex media")]);
    }

    private static NodeRow Node(string id, string kind, string displayName, string? libvirtDomain) =>
        new(id, id, kind, displayName, "managed", libvirtDomain);
}
