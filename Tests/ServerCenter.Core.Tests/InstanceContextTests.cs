using AwesomeAssertions;
using ServerCenter.Primitives.ConfigTemplating;
using Xunit;

namespace ServerCenter.Core.Tests;

// The reserved-token map that lets N instances of one game descriptor/recipe get distinct paths/units.
public sealed class InstanceContextTests
{
    [Fact]
    public void Build_injects_reserved_tokens_and_flattens_params()
    {
        Dictionary<string, string> tokens = InstanceContext.Build(
            "arena1", "web-server", """{"name":"FFA","ports":{"game":27015}}""");

        tokens["instance.id"].Should().Be("arena1");
        tokens["node.id"].Should().Be("web-server");
        tokens["instance.name"].Should().Be("FFA");     // from the params' "name"
        tokens["ports.game"].Should().Be("27015");       // flattened param still present
    }

    [Fact]
    public void Instance_name_falls_back_to_the_id_when_no_name_param()
    {
        Dictionary<string, string> tokens = InstanceContext.Build("arena1", "web-server", """{"ports":{"game":27015}}""");

        tokens["instance.name"].Should().Be("arena1");
    }
}
