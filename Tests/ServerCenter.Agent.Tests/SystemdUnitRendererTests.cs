using AwesomeAssertions;
using ServerCenter.Agent.Linux;
using ServerCenter.Core.Recipes;
using Xunit;

namespace ServerCenter.Agent.Tests;

// Rendering a recipe's ServiceDefinition into a systemd unit file (pure).
public sealed class SystemdUnitRendererTests
{
    [Fact]
    public void Render_produces_a_valid_unit_with_the_service_fields()
    {
        var unit = SystemdUnitRenderer.Render(new ServiceDefinition("cs2.service", "/opt/cs2/start.sh", "cs2"));

        unit.Should().Contain("ExecStart=/opt/cs2/start.sh");
        unit.Should().Contain("User=cs2");
        unit.Should().Contain("Restart=on-failure");
        unit.Should().Contain("WantedBy=multi-user.target");
        unit.Should().Contain("\n").And.NotContain("\r"); // unix newlines regardless of host
    }

    [Fact]
    public void Render_omits_the_user_line_when_none()
    {
        SystemdUnitRenderer.Render(new ServiceDefinition("x.service", "/x")).Should().NotContain("User=");
    }
}
