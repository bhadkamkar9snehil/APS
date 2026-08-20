using APS.Application;
using APS.Domain;

namespace APS.Planning;

/// <summary>
/// #19: turns "no finite schedule satisfies the current constraints" into a statement a planner can
/// act on. CP-SAT reports infeasibility for the model as a whole, so the constraint family
/// responsible is recovered by lifting one family at a time and re-solving: whichever lift makes the
/// plan solvable is the one that is binding. Where the horizon is the cause, the probe's own answer
/// tells us exactly how much more time the plan needs.
///
/// Probes only ever run after a genuine infeasibility, and each is capped well below the caller's
/// solver budget - diagnosis must not cost more than the plan it is explaining.
/// </summary>
internal static class ScheduleInfeasibilityDiagnostician
{
    /// <summary>Each probe gets at most this share of the caller's solver budget.</summary>
    private const int MaximumProbeSeconds = 10;

    /// <summary>How far past the requested horizon a probe looks before giving up on "needs more time".</summary>
    private const int HorizonProbeMultiplier = 4;

    public static IReadOnlyCollection<PlanningIssue> Explain(
        FiniteScheduleRequest request,
        Func<FiniteScheduleRequest, FiniteScheduleResult> solve)
    {
        var probeSeconds = Math.Clamp(request.MaxSolverSeconds, 1, MaximumProbeSeconds);
        var findings = new List<PlanningIssue>();

        var horizon = ProbeHorizon(request, solve, probeSeconds);
        if (horizon is not null) findings.Add(horizon);

        findings.AddRange(new[]
            {
                Probe(
                    request with { ResourceCalendars = Array.Empty<ResourceCalendar>(), MaxSolverSeconds = probeSeconds },
                    solve,
                    "SCHEDULE_INFEASIBLE_CALENDAR",
                    "The plan becomes feasible once resource outages and shift calendars are lifted, so a maintenance window or outage is blocking it. Move the outage, or free the affected resource."),
                Probe(
                    WithoutMaximumLags(request) with { MaxSolverSeconds = probeSeconds },
                    solve,
                    "SCHEDULE_INFEASIBLE_QUEUE_LIMIT",
                    "The plan becomes feasible once maximum queue and transfer windows between operations are lifted. A heat cannot reach its next operation inside the allowed window - typically a thermal or hot-charge limit meeting a busy downstream resource."),
                Probe(
                    request with { StabilityConstraints = Array.Empty<FiniteScheduleStabilityConstraint>(), MaxSolverSeconds = probeSeconds },
                    solve,
                    "SCHEDULE_INFEASIBLE_TIME_FENCE",
                    "The plan becomes feasible once frozen and slushy time-fence commitments are lifted, so work already committed in the frozen zone conflicts with what is being replanned. Shorten the fence or re-cut the committed work."),
                Probe(
                    request with { MaterialEvents = Array.Empty<ScheduledMaterialEvent>(), MaxSolverSeconds = probeSeconds },
                    solve,
                    "SCHEDULE_INFEASIBLE_MATERIAL_TIMING",
                    "The plan becomes feasible once time-phased material availability is lifted, so material is not the quantity that is short but when it arrives. Pull a receipt earlier or move the consuming work later."),
                Probe(
                    request with { TransitionRules = Array.Empty<TransitionRule>(), MaxSolverSeconds = probeSeconds },
                    solve,
                    "SCHEDULE_INFEASIBLE_SEQUENCING",
                    "The plan becomes feasible once grade and section transition rules are lifted, so a forbidden transition or a changeover cost is blocking the sequence. Check the transition matrix for the grades and sections in this plan.")
            }
            .Where(issue => issue is not null)
            .Cast<PlanningIssue>());

        if (findings.Count == 0)
        {
            findings.Add(new PlanningIssue(
                PlanningIssueSeverity.Warning,
                "SCHEDULE_INFEASIBLE_CAUSE_UNRESOLVED",
                "No single constraint family explains the infeasibility, so at least two are binding together. Relaxing any one of horizon, calendars, queue windows, time fences, material timing or transition rules on its own still leaves the plan unsolvable."));
        }

        return findings;
    }

    private static PlanningIssue? ProbeHorizon(
        FiniteScheduleRequest request,
        Func<FiniteScheduleRequest, FiniteScheduleResult> solve,
        int probeSeconds)
    {
        var horizon = request.HorizonEndUtc - request.HorizonStartUtc;
        var probe = solve(request with
        {
            HorizonEndUtc = request.HorizonStartUtc + horizon * HorizonProbeMultiplier,
            MaxSolverSeconds = probeSeconds
        });
        if (!probe.IsFeasible || probe.Assignments.Count == 0) return null;

        // The probe solved it, so its own last completion is what the plan actually needs. Reported as
        // the shortfall rather than the probe horizon, which is an arbitrary search bound.
        var required = probe.Assignments.Max(x => x.EndUtc);
        var shortfall = required - request.HorizonEndUtc;
        if (shortfall <= TimeSpan.Zero) return null;

        return new PlanningIssue(
            PlanningIssueSeverity.Error,
            "SCHEDULE_INFEASIBLE_HORIZON",
            $"The work in this plan does not fit the horizon. It needs until {required:yyyy-MM-dd HH:mm} UTC, " +
            $"{FormatDuration(shortfall)} beyond the horizon end of {request.HorizonEndUtc:yyyy-MM-dd HH:mm} UTC. " +
            "Extend the horizon by at least that much, move work out of this plan, or add capacity.");
    }

    private static PlanningIssue? Probe(
        FiniteScheduleRequest relaxed,
        Func<FiniteScheduleRequest, FiniteScheduleResult> solve,
        string code,
        string message) =>
        solve(relaxed).IsFeasible
            ? new PlanningIssue(PlanningIssueSeverity.Error, code, message)
            : null;

    /// <summary>
    /// Drops every upper bound on the gap between an operation and its predecessor, keeping the
    /// minimum lags and the allowed resource pairing intact - the point is to isolate the ceiling.
    /// </summary>
    private static FiniteScheduleRequest WithoutMaximumLags(FiniteScheduleRequest request) =>
        request with
        {
            Tasks = request.Tasks
                .Select(task => task with
                {
                    Dependencies = task.Dependencies
                        .Select(dependency => dependency with
                        {
                            MaximumLagMinutes = null,
                            AllowedResourcePairs = dependency.AllowedResourcePairs?
                                .Select(pair => pair with { MaximumLagMinutes = null })
                                .ToArray()
                        })
                        .ToArray()
                })
                .ToArray()
        };

    private static string FormatDuration(TimeSpan value) =>
        value.TotalHours >= 1d
            ? $"{value.TotalHours:0.#} hours"
            : $"{Math.Ceiling(value.TotalMinutes):0} minutes";
}
