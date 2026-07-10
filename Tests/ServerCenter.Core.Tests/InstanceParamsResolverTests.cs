using AwesomeAssertions;
using ServerCenter.Primitives.ConfigTemplating;
using Xunit;

namespace ServerCenter.Core.Tests;

// The class-vs-instance seam: instance params flatten to dotted tokens that the config-template
// renderer resolves. Scalars stringify; nested objects dot; arrays index.
public sealed class InstanceParamsResolverTests
{
    private const string Params = """
        {
          "name": "cs2-ffa",
          "slots": 24,
          "ranked": true,
          "ports": { "game": 27015, "rcon": 27016 },
          "rcon": { "password": "s3cr3t" },
          "tags": ["ffa", "hardcore"]
        }
        """;

    [Fact]
    public void Flatten_dots_nested_objects_and_stringifies_scalars()
    {
        var flat = InstanceParamsResolver.Flatten(Params);

        flat["name"].Should().Be("cs2-ffa");
        flat["slots"].Should().Be("24");
        flat["ranked"].Should().Be("true");
        flat["ports.game"].Should().Be("27015");
        flat["rcon.password"].Should().Be("s3cr3t");
    }

    [Fact]
    public void Flatten_indexes_arrays()
    {
        var flat = InstanceParamsResolver.Flatten(Params);

        flat["tags.0"].Should().Be("ffa");
        flat["tags.1"].Should().Be("hardcore");
    }

    [Fact]
    public void Flattened_params_render_a_descriptor_template()
    {
        var flat = InstanceParamsResolver.Flatten(Params);

        ConfigTemplateRenderer.Render("hostname={{name}}\nport={{ports.game}}", flat)
            .Should().Be("hostname=cs2-ffa\nport=27015");
    }
}
