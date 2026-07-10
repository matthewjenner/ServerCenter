using AwesomeAssertions;
using ServerCenter.Core.Updates;
using Xunit;
using static ServerCenter.Core.Updates.UpdatePolicyResolver;

namespace ServerCenter.Core.Tests;

// The policy brain (phase-0-contracts.md 5). Pure decisions over declarative data: eligibility
// (window vs manual, plus operator override), the approval gate, preflight ordering, and the
// post-apply reboot decision. All the "when/reboot/approval as data" behavior lands here.
public sealed class UpdatePolicyResolverTests
{
    // 04:00 UTC daily; a 120-minute window means eligible from 04:00 to 06:00 UTC.
    private static UpdatePolicy Windowed(
        ApprovalMode approval = ApprovalMode.Auto, IReadOnlyList<PreflightStep>? preflight = null) => new()
    {
        Id = "plex-lowtraffic",
        Version = 1,
        What = new UpdateWhat("plex"),
        When = new UpdateSchedule { Mode = ScheduleMode.Window, Cron = "0 4 * * *", WindowMinutes = 120 },
        Approval = approval,
        Preflight = preflight ?? []
    };

    private static readonly DateTimeOffset InWindow = new(2026, 7, 10, 4, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OutOfWindow = new(2026, 7, 10, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Scheduled_tick_inside_the_window_is_eligible()
    {
        var decision = DecideStart(Windowed(), Trigger.Scheduled, InWindow);

        decision.Eligible.Should().BeTrue();
        decision.IneligibleReason.Should().BeNull();
    }

    [Fact]
    public void Scheduled_tick_outside_the_window_is_not_eligible()
    {
        var decision = DecideStart(Windowed(), Trigger.Scheduled, OutOfWindow);

        decision.Eligible.Should().BeFalse();
        decision.IneligibleReason.Should().Contain("window");
    }

    [Fact]
    public void Manual_trigger_overrides_the_window()
    {
        var decision = DecideStart(Windowed(), Trigger.Manual, OutOfWindow);

        decision.Eligible.Should().BeTrue();
    }

    [Fact]
    public void Manual_schedule_never_runs_on_a_scheduled_tick()
    {
        var policy = Windowed() with { When = UpdateSchedule.Manual };

        var decision = DecideStart(policy, Trigger.Scheduled, InWindow);

        decision.Eligible.Should().BeFalse();
        decision.IneligibleReason.Should().Contain("manual");
    }

    [Fact]
    public void Manual_schedule_runs_on_an_explicit_trigger()
    {
        var policy = Windowed() with { When = UpdateSchedule.Manual };

        DecideStart(policy, Trigger.Manual, OutOfWindow).Eligible.Should().BeTrue();
    }

    [Fact]
    public void Require_confirmation_gates_a_scheduled_run()
    {
        var decision = DecideStart(Windowed(ApprovalMode.RequireConfirmation), Trigger.Scheduled, InWindow);

        decision.Eligible.Should().BeTrue();
        decision.RequiresConfirmation.Should().BeTrue();
    }

    [Fact]
    public void An_explicit_trigger_is_its_own_confirmation()
    {
        var decision = DecideStart(Windowed(ApprovalMode.RequireConfirmation), Trigger.Manual, InWindow);

        decision.RequiresConfirmation.Should().BeFalse();
    }

    [Fact]
    public void Preflight_is_deduped_preserving_declared_order()
    {
        var policy = Windowed(preflight:
            [PreflightStep.Notify, PreflightStep.DrainPlayersViaRcon, PreflightStep.Notify]);

        var decision = DecideStart(policy, Trigger.Manual, InWindow);

        decision.Preflight.Should().Equal(PreflightStep.Notify, PreflightStep.DrainPlayersViaRcon);
    }

    [Fact]
    public void A_broken_cron_fails_closed_on_a_scheduled_tick()
    {
        var policy = Windowed() with
        {
            When = new UpdateSchedule { Mode = ScheduleMode.Window, Cron = "not a cron", WindowMinutes = 120 }
        };

        DecideStart(policy, Trigger.Scheduled, InWindow).Eligible.Should().BeFalse();
    }

    [Theory]
    [InlineData(RebootPolicy.Never, true, RebootAction.None)]
    [InlineData(RebootPolicy.Never, false, RebootAction.None)]
    [InlineData(RebootPolicy.IfRequired, true, RebootAction.Reboot)]
    [InlineData(RebootPolicy.IfRequired, false, RebootAction.None)]
    [InlineData(RebootPolicy.AlwaysAfter, false, RebootAction.Reboot)]
    [InlineData(RebootPolicy.AlwaysAfter, true, RebootAction.Reboot)]
    [InlineData(RebootPolicy.Prompt, true, RebootAction.PromptOperator)]
    [InlineData(RebootPolicy.Prompt, false, RebootAction.None)]
    public void ResolveReboot_covers_the_policy_matrix(
        RebootPolicy policy, bool rebootRequired, RebootAction expected)
    {
        ResolveReboot(policy, rebootRequired).Should().Be(expected);
    }
}
