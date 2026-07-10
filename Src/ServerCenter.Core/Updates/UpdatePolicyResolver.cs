using Cronos;

namespace ServerCenter.Core.Updates;

// Turns a declarative UpdatePolicy into decisions. Pure and side-effect-free so the controller's
// dispatch path and Tier 1 tests share one source of truth for policy semantics. Two decisions:
//   - DecideStart: may this policy run now, does it need operator confirmation first, and in what
//     order do the preflight steps run.
//   - ResolveReboot: given the post-apply reboot-required truth, what to do about rebooting.
public static class UpdatePolicyResolver
{
    // Was the run asked for by an explicit operator action, or by an autonomous scheduler tick?
    public enum Trigger
    {
        Scheduled,
        Manual
    }

    public sealed record StartDecision(
        bool Eligible,
        bool RequiresConfirmation,
        IReadOnlyList<PreflightStep> Preflight,
        string? IneligibleReason);

    public enum RebootAction
    {
        None,
        Reboot,
        PromptOperator
    }

    public static StartDecision DecideStart(UpdatePolicy policy, Trigger trigger, DateTimeOffset now)
    {
        var preflight = Dedupe(policy.Preflight);

        // An explicit operator trigger overrides the schedule window (the operator is choosing to
        // run now) and itself counts as the confirmation. A scheduler tick must respect both the
        // window and the approval gate.
        if (trigger == Trigger.Manual)
        {
            return new StartDecision(Eligible: true, RequiresConfirmation: false, preflight, IneligibleReason: null);
        }

        var requiresConfirmation = policy.Approval == ApprovalMode.RequireConfirmation;

        if (policy.When.Mode == ScheduleMode.Manual)
        {
            return new StartDecision(
                Eligible: false, requiresConfirmation, preflight,
                "policy schedule is manual; requires an explicit operator trigger");
        }

        return WithinWindow(policy.When, now)
            ? new StartDecision(Eligible: true, requiresConfirmation, preflight, IneligibleReason: null)
            : new StartDecision(Eligible: false, requiresConfirmation, preflight, "outside the policy update window");
    }

    public static RebootAction ResolveReboot(RebootPolicy policy, bool rebootRequired) => policy switch
    {
        RebootPolicy.Never => RebootAction.None,
        RebootPolicy.IfRequired => rebootRequired ? RebootAction.Reboot : RebootAction.None,
        RebootPolicy.AlwaysAfter => RebootAction.Reboot,
        RebootPolicy.Prompt => rebootRequired ? RebootAction.PromptOperator : RebootAction.None,
        _ => RebootAction.None
    };

    // A window is open at `now` if a cron occurrence fell within the last WindowMinutes, i.e. some
    // occurrence T satisfies now-WindowMinutes < T <= now. Cron is evaluated in UTC. A malformed
    // cron or a non-positive window means "never open" (fail closed - a broken policy never fires
    // autonomously).
    private static bool WithinWindow(UpdateSchedule when, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(when.Cron) || when.WindowMinutes is not > 0)
        {
            return false;
        }

        CronExpression expression;
        try
        {
            expression = CronExpression.Parse(when.Cron, CronFormat.Standard);
        }
        catch (CronFormatException)
        {
            return false;
        }

        var nowUtc = now.UtcDateTime;
        var windowStart = nowUtc.AddMinutes(-when.WindowMinutes.Value);
        return expression
            .GetOccurrences(windowStart, nowUtc, fromInclusive: false, toInclusive: true)
            .Any();
    }

    private static IReadOnlyList<PreflightStep> Dedupe(IReadOnlyList<PreflightStep> steps)
    {
        var seen = new HashSet<PreflightStep>();
        var ordered = new List<PreflightStep>(steps.Count);
        foreach (var step in steps)
        {
            if (seen.Add(step))
            {
                ordered.Add(step);
            }
        }

        return ordered;
    }
}
