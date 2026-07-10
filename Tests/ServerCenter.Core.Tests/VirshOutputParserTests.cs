using AwesomeAssertions;
using ServerCenter.Core.Primitives;
using ServerCenter.Primitives.Libvirt;
using Xunit;

namespace ServerCenter.Core.Tests;

// Parsing virsh text into the libvirt model (the real virsh runs at Tier 3 only).
public sealed class VirshOutputParserTests
{
    [Theory]
    [InlineData("running", DomainState.Running)]
    [InlineData("paused", DomainState.Paused)]
    [InlineData("shut off", DomainState.ShutOff)]
    [InlineData("in shutdown", DomainState.Shutdown)]
    [InlineData("crashed", DomainState.Crashed)]
    [InlineData("something-weird", DomainState.Unknown)]
    public void ParseState_maps_virsh_state_text(string text, DomainState expected)
    {
        VirshOutputParser.ParseState(text).Should().Be(expected);
    }

    [Fact]
    public void ParseDomainList_reads_names_and_multiword_states()
    {
        const string output =
            " Id   Name       State\n" +
            "----------------------------\n" +
            " 1    cs2-ffa    running\n" +
            " -    plex-vm    shut off\n";

        var domains = VirshOutputParser.ParseDomainList(output);

        domains.Should().HaveCount(2);
        domains[0].Name.Should().Be("cs2-ffa");
        domains[0].State.Should().Be(DomainState.Running);
        domains[1].Name.Should().Be("plex-vm");
        domains[1].State.Should().Be(DomainState.ShutOff); // multi-word state
    }

    [Fact]
    public void ParseDomInfo_reads_name_uuid_and_state()
    {
        const string output =
            "Id:             1\n" +
            "Name:           cs2-ffa\n" +
            "UUID:           4dea22b3-1d52-d8f3-2516-782e98ab3fa0\n" +
            "OS Type:        hvm\n" +
            "State:          running\n" +
            "CPU(s):         4\n";

        var domain = VirshOutputParser.ParseDomInfo(output);

        domain.Should().Be(new DomainInfo("cs2-ffa", "4dea22b3-1d52-d8f3-2516-782e98ab3fa0", DomainState.Running));
    }

    [Fact]
    public void ParseDomInfo_is_null_without_a_name()
    {
        VirshOutputParser.ParseDomInfo("error: failed to get domain 'nope'").Should().BeNull();
    }
}
