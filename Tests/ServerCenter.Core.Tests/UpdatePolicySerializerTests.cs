using AwesomeAssertions;
using ServerCenter.Core.Updates;
using Xunit;

namespace ServerCenter.Core.Tests;

// The stored policy body must match the hand-authorable schema in the brief (kebab-case enum
// tokens, camelCase properties) and round-trip losslessly, since the body_json IS the source of
// truth for a policy revision.
public sealed class UpdatePolicySerializerTests
{
    private static readonly UpdatePolicy Sample = new()
    {
        Id = "plex-lowtraffic",
        Version = 2,
        What = new UpdateWhat("plex"),
        How = UpdateHow.StopUpdateStart,
        When = new UpdateSchedule { Mode = ScheduleMode.Window, Cron = "0 4 * * *", WindowMinutes = 120 },
        Reboot = RebootPolicy.IfRequired,
        Preflight = [PreflightStep.Notify],
        Approval = ApprovalMode.RequireConfirmation
    };

    [Fact]
    public void Serialize_emits_the_brief_tokens()
    {
        string json = UpdatePolicySerializer.Serialize(Sample);

        json.Should().Contain("\"how\":\"stop-update-start\"");
        json.Should().Contain("\"reboot\":\"if-required\"");
        json.Should().Contain("\"approval\":\"require-confirmation\"");
        json.Should().Contain("\"windowMinutes\":120");
    }

    [Fact]
    public void Round_trips_losslessly()
    {
        UpdatePolicy restored = UpdatePolicySerializer.Deserialize(UpdatePolicySerializer.Serialize(Sample));

        // BeEquivalentTo (deep/structural) not Be: the record's IReadOnlyList Preflight compares by
        // reference under record equality, so two equal-but-distinct lists would fail Be().
        restored.Should().BeEquivalentTo(Sample);
    }

    [Fact]
    public void Manual_schedule_omits_null_cron_and_window()
    {
        string json = UpdatePolicySerializer.Serialize(Sample with { When = UpdateSchedule.Manual });

        json.Should().Contain("\"mode\":\"manual\"");
        json.Should().NotContain("cron");
        json.Should().NotContain("windowMinutes");
    }
}
