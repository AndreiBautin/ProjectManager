using ProjectManager.Api.Models;
using ProjectManager.Api.Services;

namespace ProjectManager.Tests;

/// <summary>
/// PriorityEngine holds every rule this app exists to enforce - what to work on
/// next, and why. It is a pure static class over in-memory objects, so it needs
/// no database, no HTTP and no fixtures. That makes it both the highest-value
/// and the cheapest thing in the repository to test, which is why it is covered
/// first and most thoroughly.
/// </summary>
public class PriorityEngineTests
{
    private static Project P(
        string name = "P", int impact = 5, int urgency = 5, int effort = 5,
        ProjectStatus status = ProjectStatus.Active,
        DateTime? deadline = null,
        bool isBlocked = false,
        DateTime? created = null,
        params ActionItem[] actions) => new()
        {
            Name = name,
            Impact = impact,
            Urgency = urgency,
            Effort = effort,
            Status = status,
            Deadline = deadline,
            IsBlocked = isBlocked,
            CreatedDate = created ?? new DateTime(2026, 1, 1),
            Actions = actions.ToList(),
        };

    private static ActionItem A(
        string description = "do the thing", int order = 1,
        ActionStatus status = ActionStatus.Pending,
        DateTime? availableFrom = null) => new()
        {
            Description = description,
            Order = order,
            Status = status,
            AvailableFrom = availableFrom,
        };

    // ---- Scoring --------------------------------------------------------

    [Theory]
    [InlineData(10, 10, 1, 100)]   // maximum possible score
    [InlineData(1, 1, 10, 0)]      // minimum: 0.1 rounds to 0
    [InlineData(5, 5, 5, 5)]       // the defaults
    [InlineData(9, 8, 4, 18)]      // 72/4 exactly
    [InlineData(10, 3, 4, 8)]      // 7.5 rounds to 8 (banker's rounding would give 8 too)
    public void ComputeScore_appliesImpactTimesUrgencyOverEffort(int impact, int urgency, int effort, int expected)
    {
        Assert.Equal(expected, PriorityEngine.ComputeScore(P(impact: impact, urgency: urgency, effort: effort)));
    }

    [Fact]
    public void ComputeScore_doesNotDivideByZero_whenEffortIsZero()
    {
        // The design doc calls this out as a flaw in the original spec. Effort is
        // floored at 1 in the engine regardless of what reaches it, so even a
        // record written directly to the database cannot produce a DivideByZero.
        var score = PriorityEngine.ComputeScore(P(impact: 8, urgency: 6, effort: 0));
        Assert.Equal(48, score);
    }

    // ---- Deadline ramp --------------------------------------------------

    [Fact]
    public void EffectiveUrgency_isUnchanged_whenThereIsNoDeadline()
    {
        Assert.Equal(4, PriorityEngine.ComputeEffectiveUrgency(P(urgency: 4)));
    }

    [Fact]
    public void EffectiveUrgency_isUnchanged_whenTheDeadlineIsOutsideTheRampWindow()
    {
        var far = DateTime.Now.Date.AddDays(30);
        Assert.Equal(4, PriorityEngine.ComputeEffectiveUrgency(P(urgency: 4, deadline: far)));
    }

    [Fact]
    public void EffectiveUrgency_pinsAtTen_onAndAfterTheDeadline()
    {
        Assert.Equal(10, PriorityEngine.ComputeEffectiveUrgency(P(urgency: 1, deadline: DateTime.Now.Date)));
        Assert.Equal(10, PriorityEngine.ComputeEffectiveUrgency(P(urgency: 1, deadline: DateTime.Now.Date.AddDays(-5))));
    }

    [Fact]
    public void EffectiveUrgency_rampsProportionallyInsideTheWindow()
    {
        // 7 days out is exactly halfway through the 14-day ramp, so urgency 4
        // should sit halfway between 4 and 10.
        var value = PriorityEngine.ComputeEffectiveUrgency(P(urgency: 4, deadline: DateTime.Now.Date.AddDays(7)));
        Assert.Equal(7.0, value, precision: 6);
    }

    [Fact]
    public void EffectiveUrgency_onlyEverIncreases_neverDecreases()
    {
        // A manually-set 10 must not be dragged down by the ramp arithmetic.
        for (var days = 0; days <= 20; days++)
        {
            var project = P(urgency: 10, deadline: DateTime.Now.Date.AddDays(days));
            Assert.True(PriorityEngine.ComputeEffectiveUrgency(project) >= 10);
        }
    }

    // ---- Progress -------------------------------------------------------

    [Fact]
    public void Progress_isZero_whenThereAreNoActions()
    {
        Assert.Equal(0, PriorityEngine.ComputeProgress(P()));
    }

    [Fact]
    public void Progress_isTheShareOfActionsDone()
    {
        var project = P(actions: new[]
        {
            A(order: 1, status: ActionStatus.Done),
            A(order: 2, status: ActionStatus.Done),
            A(order: 3),
            A(order: 4),
        });
        Assert.Equal(50, PriorityEngine.ComputeProgress(project));
    }

    [Fact]
    public void Progress_isAlwaysOneHundred_forACompletedProject()
    {
        // A project can be closed out with the button before every action is
        // ticked; it should still read 100%, not 33%.
        var project = P(status: ProjectStatus.Completed, actions: new[]
        {
            A(order: 1, status: ActionStatus.Done), A(order: 2), A(order: 3),
        });
        Assert.Equal(100, PriorityEngine.ComputeProgress(project));
    }

    // ---- Eligibility ----------------------------------------------------

    [Fact]
    public void Eligibility_nullActionIsNotEligible()
        => Assert.False(PriorityEngine.IsEligibleNow(null));

    [Fact]
    public void Eligibility_actionWithNoDateIsAlwaysEligible()
        => Assert.True(PriorityEngine.IsEligibleNow(A()));

    [Fact]
    public void Eligibility_futureDatedActionIsNotEligibleYet()
        => Assert.False(PriorityEngine.IsEligibleNow(A(availableFrom: DateTime.Now.Date.AddDays(1))));

    [Fact]
    public void Eligibility_actionAvailableFromTodayIsEligible()
        => Assert.True(PriorityEngine.IsEligibleNow(A(availableFrom: DateTime.Now.Date)));

    // ---- Next action ----------------------------------------------------

    [Fact]
    public void NextAction_isTheLowestOrderedPendingAction_skippingDoneOnes()
    {
        var project = P(actions: new[]
        {
            A("third", order: 3),
            A("first", order: 1, status: ActionStatus.Done),
            A("second", order: 2),
        });
        Assert.Equal("second", PriorityEngine.GetCurrentNextAction(project)?.Description);
    }

    [Fact]
    public void NextAction_isNull_whenEverythingIsDone()
    {
        var project = P(actions: new[] { A(order: 1, status: ActionStatus.Done) });
        Assert.Null(PriorityEngine.GetCurrentNextAction(project));
    }

    // ---- Ranking --------------------------------------------------------

    [Fact]
    public void Ranking_excludesPausedAndCompletedProjects()
    {
        var ranked = PriorityEngine.RankActiveProjects(new[]
        {
            P("active"),
            P("paused", status: ProjectStatus.Paused),
            P("completed", status: ProjectStatus.Completed),
            P("blocked", status: ProjectStatus.Blocked),
        });

        Assert.Equal(new[] { "active", "blocked" }, ranked.Select(p => p.Name).OrderBy(n => n).ToArray());
    }

    [Fact]
    public void Ranking_ordersByScoreDescending()
    {
        var ranked = PriorityEngine.RankActiveProjects(new[]
        {
            P("low", impact: 1, urgency: 1, effort: 10),
            P("high", impact: 10, urgency: 10, effort: 1),
            P("mid", impact: 5, urgency: 5, effort: 5),
        });

        Assert.Equal(new[] { "high", "mid", "low" }, ranked.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void Ranking_breaksScoreTiesWithUrgencyThenOldestFirst()
    {
        // All three score 10. Higher urgency wins; equal urgency falls back to
        // the older CreatedDate, so nothing quietly rots at the bottom.
        var older = new DateTime(2025, 1, 1);
        var newer = new DateTime(2026, 1, 1);

        var ranked = PriorityEngine.RankActiveProjects(new[]
        {
            P("equal-newer", impact: 5, urgency: 4, effort: 2, created: newer),
            P("higher-urgency", impact: 2, urgency: 10, effort: 2, created: newer),
            P("equal-older", impact: 5, urgency: 4, effort: 2, created: older),
        });

        Assert.Equal(new[] { "higher-urgency", "equal-older", "equal-newer" }, ranked.Select(p => p.Name).ToArray());
    }

    // ---- Recommendation -------------------------------------------------

    [Fact]
    public void Recommendation_picksTheTopRankedProjectsNextAction()
    {
        var result = PriorityEngine.GetRecommendation(new[]
        {
            P("low", impact: 1, urgency: 1, effort: 10, actions: A("do the small thing")),
            P("high", impact: 10, urgency: 10, effort: 1, actions: A("do the big thing")),
        });

        Assert.Equal("high", result.ProjectName);
        Assert.Equal("do the big thing", result.ActionDescription);
        Assert.Equal("Highest priority active project", result.Reason);
    }

    [Fact]
    public void Recommendation_skipsProjectsWithNoNextAction()
    {
        var result = PriorityEngine.GetRecommendation(new[]
        {
            P("stuck-but-top", impact: 10, urgency: 10, effort: 1),
            P("runner-up", impact: 5, urgency: 5, effort: 5, actions: A("the reachable thing")),
        });

        Assert.Equal("runner-up", result.ProjectName);
    }

    [Fact]
    public void Recommendation_skipsProjectsWhoseNextActionIsNotAvailableYet()
    {
        var result = PriorityEngine.GetRecommendation(new[]
        {
            P("waiting", impact: 10, urgency: 10, effort: 1,
                actions: A("wait for the part", availableFrom: DateTime.Now.Date.AddDays(3))),
            P("workable", impact: 5, urgency: 5, effort: 5, actions: A("the doable thing")),
        });

        Assert.Equal("workable", result.ProjectName);
    }

    [Fact]
    public void Recommendation_stillRecommendsAManuallyBlockedProject_becauseItsNextActionIsTheUnblockStep()
    {
        var result = PriorityEngine.GetRecommendation(new[]
        {
            P("blocked", impact: 10, urgency: 10, effort: 1,
                status: ProjectStatus.Blocked, isBlocked: true,
                actions: A("chase the records office")),
        });

        Assert.Equal("blocked", result.ProjectName);
        Assert.Equal("Unblocks a high-priority project", result.Reason);
    }

    [Fact]
    public void Recommendation_skipsAProjectBlockedByAnotherOpenProject()
    {
        // Its own next action does not release it - finishing the other project
        // does - so recommending it would be telling you to do the wrong thing.
        var blocker = P("blocker", status: ProjectStatus.Active);
        var blocked = P("blocked-by-project", impact: 10, urgency: 10, effort: 1,
            status: ProjectStatus.Blocked, actions: A("its own step"));
        blocked.Blockers.Add(new ProjectBlocker { BlockingProject = blocker });

        var fallback = P("fallback", impact: 5, urgency: 5, effort: 5, actions: A("do this instead"));

        var result = PriorityEngine.GetRecommendation(new[] { blocked, fallback });

        Assert.Equal("fallback", result.ProjectName);
    }

    [Fact]
    public void Recommendation_stopsSkipping_onceTheBlockingProjectIsCompleted()
    {
        var blocker = P("blocker", status: ProjectStatus.Completed);
        var blocked = P("was-blocked", impact: 10, urgency: 10, effort: 1, actions: A("now workable"));
        blocked.Blockers.Add(new ProjectBlocker { BlockingProject = blocker });

        var result = PriorityEngine.GetRecommendation(new[] { blocked });

        Assert.Equal("was-blocked", result.ProjectName);
    }

    [Fact]
    public void Recommendation_returnsAnExplanation_notNulls_whenNothingIsActionable()
    {
        var result = PriorityEngine.GetRecommendation(Array.Empty<Project>());

        Assert.Null(result.ProjectId);
        Assert.Null(result.ActionId);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    // ---- Status derivation ----------------------------------------------

    [Theory]
    [InlineData(ProjectStatus.Completed)]
    [InlineData(ProjectStatus.Paused)]
    public void DeriveStatus_passesThroughExplicitUserChoices(ProjectStatus requested)
    {
        var project = P(isBlocked: true);
        Assert.Equal(requested, PriorityEngine.DeriveStatus(project, requested));
    }

    [Fact]
    public void DeriveStatus_returnsBlocked_whenTheManualFlagIsSet()
    {
        Assert.Equal(ProjectStatus.Blocked, PriorityEngine.DeriveStatus(P(isBlocked: true), ProjectStatus.Active));
    }

    [Fact]
    public void DeriveStatus_returnsBlocked_whenWaitingOnAnOpenProject()
    {
        var project = P();
        project.Blockers.Add(new ProjectBlocker { BlockingProject = P("other", status: ProjectStatus.Active) });

        Assert.Equal(ProjectStatus.Blocked, PriorityEngine.DeriveStatus(project, ProjectStatus.Active));
    }

    [Fact]
    public void DeriveStatus_returnsActive_onceEveryBlockingProjectIsCompleted()
    {
        var project = P();
        project.Blockers.Add(new ProjectBlocker { BlockingProject = P("a", status: ProjectStatus.Completed) });
        project.Blockers.Add(new ProjectBlocker { BlockingProject = P("b", status: ProjectStatus.Completed) });

        Assert.Equal(ProjectStatus.Active, PriorityEngine.DeriveStatus(project, ProjectStatus.Blocked));
    }
}
