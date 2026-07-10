using AwesomeAssertions;
using ServerCenter.Primitives.ConfigTemplating;
using Xunit;

namespace ServerCenter.Core.Tests;

// The config-templating primitive is the class-vs-instance seam: a shared template rendered
// with per-server instance params. Missing token must fail loudly, not render half a config.
public sealed class ConfigTemplateRendererTests
{
    [Fact]
    public void Render_substitutes_known_tokens()
    {
        string result = ConfigTemplateRenderer.Render(
            "hostname={{name}}\nport={{ports.game}}",
            new Dictionary<string, string> { ["name"] = "cs2-ffa", ["ports.game"] = "27015" });

        result.Should().Be("hostname=cs2-ffa\nport=27015");
    }

    [Fact]
    public void Render_throws_on_unknown_token()
    {
        Func<string> act = () => ConfigTemplateRenderer.Render(
            "rcon={{rcon.password}}",
            new Dictionary<string, string>());

        act.Should().Throw<KeyNotFoundException>();
    }
}
