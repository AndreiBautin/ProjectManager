using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManager.Api.Data;
using ProjectManager.Api.Demo;
using ProjectManager.Api.Models;
using ProjectManager.Api.Services;

namespace ProjectManager.Tests;

/// <summary>
/// The demo dataset is what a public deployment serves, so these are the tests
/// with the highest consequence of failure in the repository. They assert the
/// three barriers protecting personal data rather than trusting that they hold.
/// </summary>
public class DemoDataTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    // A fixed instant, so every assertion about relative dates is deterministic.
    private static readonly DateTime Now = new(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    public DemoDataTests()
    {
        // In-memory SQLite rather than the EF in-memory provider: it exercises
        // the real relational behaviour - cascades, unique indexes, FKs - which
        // is exactly what the seeder depends on.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewContext()
    {
        var db = new AppDbContext(_options);
        DbSeeder.Seed(db);
        return db;
    }

    // =====================================================================
    // Barrier 1: the fixture contains nothing that looks like personal data
    // =====================================================================

    private static IEnumerable<string> AllFixtureText()
    {
        var categories = new Dictionary<string, int>
        {
            ["Home"] = 1, ["Career"] = 2, ["Finance"] = 3,
            ["Personal"] = 4, ["Relationships"] = 5, ["Hobbies"] = 6,
        };

        var fixture = DemoDataFixture.Build(Now, categories);

        foreach (var project in fixture.Projects)
        {
            yield return project.Name;
            if (project.Description != null) yield return project.Description;
            if (project.BlockReason != null) yield return project.BlockReason;
            foreach (var action in project.Actions) yield return action.Description;
        }
    }

    [Theory]
    // Anything shaped like an email address.
    [InlineData(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", "email address")]
    // Phone numbers, in the common separated and grouped forms.
    [InlineData(@"(\+\d{1,3}[ .-]?)?\(?\d{3}\)?[ .-]\d{3}[ .-]\d{4}", "phone number")]
    // URLs - a demo fixture should not be pointing anywhere.
    [InlineData(@"https?://\S+", "URL")]
    [InlineData(@"\bwww\.\S+", "URL")]
    // Government-ID shapes.
    [InlineData(@"\b\d{3}-\d{2}-\d{4}\b", "government ID")]
    // Long digit runs: card numbers, account numbers, IDs.
    [InlineData(@"\b\d{9,}\b", "account or card number")]
    // Credential-looking assignments.
    [InlineData(@"(?i)\b(api[_-]?key|secret|password|passwd|token|bearer)\b\s*[:=]", "credential")]
    // Private key material.
    [InlineData(@"-----BEGIN [A-Z ]*PRIVATE KEY-----", "private key")]
    public void Fixture_containsNothingMatchingPersonalDataPatterns(string pattern, string description)
    {
        var regex = new Regex(pattern);

        var offenders = AllFixtureText().Where(text => regex.IsMatch(text)).ToList();

        Assert.True(offenders.Count == 0,
            $"Demo fixture contains something matching a {description} pattern: {string.Join(" | ", offenders)}");
    }

    [Fact]
    public void Fixture_doesNotMentionTheRealDatabaseFile()
    {
        // A cheap canary: if a real record were ever pasted in from the personal
        // database, its provenance often comes with it.
        Assert.DoesNotContain(AllFixtureText(), t => t.Contains("projectmanager.db", StringComparison.OrdinalIgnoreCase));
    }

    // =====================================================================
    // Barrier 3: seeding only ever fills empty storage
    // =====================================================================

    [Fact]
    public void SeedIfEmpty_populatesAnEmptyDatabase()
    {
        using var db = NewContext();

        var outcome = DemoDataSeeder.SeedIfEmpty(db, Now);

        Assert.Equal(DemoSeedOutcome.Seeded, outcome);
        Assert.True(db.Projects.Count() > 10);
    }

    [Fact]
    public void SeedIfEmpty_writesNothing_whenAnyProjectAlreadyExists()
    {
        using var db = NewContext();
        db.Projects.Add(new Project { Name = "Something the user already had" });
        db.SaveChanges();

        var outcome = DemoDataSeeder.SeedIfEmpty(db, Now);

        Assert.Equal(DemoSeedOutcome.SkippedNotEmpty, outcome);
        Assert.Equal(1, db.Projects.Count());
    }

    [Fact]
    public void SeedIfEmpty_neverDestroysExistingData_evenWhenCalledRepeatedly()
    {
        // This is the property that makes it safe to run on every startup: it is
        // idempotent in the only direction that matters.
        using var db = NewContext();
        db.Projects.Add(new Project { Name = "Precious" });
        db.SaveChanges();

        for (var i = 0; i < 5; i++) DemoDataSeeder.SeedIfEmpty(db, Now);

        Assert.Equal(1, db.Projects.Count());
        Assert.Equal("Precious", db.Projects.Single().Name);
    }

    [Fact]
    public void SeedIfEmpty_isIdempotent_onAFreshDatabase()
    {
        using var db = NewContext();

        DemoDataSeeder.SeedIfEmpty(db, Now);
        var afterFirst = db.Projects.Count();
        DemoDataSeeder.SeedIfEmpty(db, Now);

        Assert.Equal(afterFirst, db.Projects.Count());
    }

    [Fact]
    public void ResetToDemo_isTheOnlyOperationThatDeletes()
    {
        using var db = NewContext();
        db.Projects.Add(new Project { Name = "Will be replaced" });
        db.SaveChanges();

        var outcome = DemoDataSeeder.ResetToDemo(db, Now);

        Assert.Equal(DemoSeedOutcome.Reset, outcome);
        Assert.DoesNotContain(db.Projects, p => p.Name == "Will be replaced");
        Assert.True(db.Projects.Count() > 10);
    }

    // =====================================================================
    // The dataset is actually good: legible, complete, and it does not rot
    // =====================================================================

    [Fact]
    public void Fixture_isDeterministicForAGivenNow()
    {
        var categories = new Dictionary<string, int> { ["Home"] = 1 };

        var a = DemoDataFixture.Build(Now, categories);
        var b = DemoDataFixture.Build(Now, categories);

        Assert.Equal(
            a.Projects.Select(p => (p.Name, p.CreatedDate, p.Deadline)),
            b.Projects.Select(p => (p.Name, p.CreatedDate, p.Deadline)));
    }

    [Theory]
    [InlineData(2026)]
    [InlineData(2029)]
    [InlineData(2040)]
    public void Fixture_doesNotRot_whicheverYearItIsOpenedIn(int year)
    {
        // A fixture pinned to absolute dates shows dead streaks and empty "this
        // month" statistics once it ages. Because every date is an offset from
        // `now`, all three Completed-page counters stay non-zero forever.
        var now = new DateTime(year, 3, 9, 8, 0, 0, DateTimeKind.Utc);
        var fixture = DemoDataFixture.Build(now, new Dictionary<string, int>());

        var completed = fixture.Projects
            .Where(p => p.Status == ProjectStatus.Completed && p.CompletedDate.HasValue)
            .Select(p => (now - p.CompletedDate!.Value).TotalDays)
            .ToList();

        Assert.True(completed.Count(d => d <= 30) >= 1, "no project completed within the last 30 days");
        Assert.True(completed.Count(d => d <= 90) >= 3, "fewer than 3 projects completed within the last 90 days");
        Assert.True(completed.Count > 5, "too few completed projects overall");
    }

    [Fact]
    public void Fixture_coversEveryProjectStatus()
    {
        var fixture = DemoDataFixture.Build(Now, new Dictionary<string, int>());
        var statuses = fixture.Projects.Select(p => p.Status).Distinct().ToList();

        Assert.Contains(ProjectStatus.Active, statuses);
        Assert.Contains(ProjectStatus.Blocked, statuses);
        Assert.Contains(ProjectStatus.Paused, statuses);
        Assert.Contains(ProjectStatus.Completed, statuses);
    }

    [Fact]
    public void Fixture_coversEveryCategory()
    {
        var categories = new Dictionary<string, int>
        {
            ["Home"] = 1, ["Career"] = 2, ["Finance"] = 3,
            ["Personal"] = 4, ["Relationships"] = 5, ["Hobbies"] = 6,
        };

        var used = DemoDataFixture.Build(Now, categories)
            .Projects.Where(p => p.CategoryId.HasValue).Select(p => p.CategoryId!.Value).Distinct().ToList();

        Assert.Equal(categories.Count, used.Count);
    }

    [Fact]
    public void Fixture_coversEveryUiStatePane()
    {
        var fixture = DemoDataFixture.Build(Now, new Dictionary<string, int>());
        var projects = fixture.Projects;

        // Green: active, with a workable next action.
        Assert.Contains(projects, p => p.Status == ProjectStatus.Active
            && PriorityEngine.IsEligibleNow(PriorityEngine.GetCurrentNextAction(p)));

        // Blue: a next action that exists but is date-gated into the future.
        Assert.Contains(projects, p => p.Actions.Any(a =>
            a.Status == ActionStatus.Pending && a.AvailableFrom.HasValue && a.AvailableFrom > Now));

        // Amber: manually blocked, but its own next action is the unblock step.
        Assert.Contains(projects, p => p.IsBlocked && p.Actions.Any(a => a.Status == ActionStatus.Pending));

        // Red: blocked with no defined path forward at all.
        Assert.Contains(projects, p => p.IsBlocked && p.Actions.Count == 0);

        // Gray: an active project nobody has given a next step.
        Assert.Contains(projects, p => p.Status == ProjectStatus.Active && p.Actions.Count == 0);

        // Purple: waiting on another tracked project.
        Assert.NotEmpty(fixture.BlockerLinks);
    }

    [Fact]
    public void Fixture_coversDeadlinesOnBothSidesOfTheRampWindow()
    {
        var fixture = DemoDataFixture.Build(Now, new Dictionary<string, int>());
        var deadlines = fixture.Projects.Where(p => p.Deadline.HasValue).Select(p => p.Deadline!.Value).ToList();

        Assert.Contains(deadlines, d => d < Now.Date);                                  // overdue
        Assert.Contains(deadlines, d => d >= Now.Date && (d - Now.Date).TotalDays < 14); // ramping
    }

    [Fact]
    public void Fixture_includesTheMinimalRecordEdgeCase()
    {
        var fixture = DemoDataFixture.Build(Now, new Dictionary<string, int>());

        Assert.Contains(fixture.Projects, p =>
            p.Description == null && p.CategoryId == null && p.Deadline == null && p.Actions.Count == 0);
    }

    [Fact]
    public void Fixture_includesLongValuesToProveTheLayoutHolds()
    {
        var fixture = DemoDataFixture.Build(Now, new Dictionary<string, int>());

        Assert.Contains(fixture.Projects, p => p.Name.Length > 100);
        Assert.Contains(fixture.Projects, p => (p.Description?.Length ?? 0) > 300);
        Assert.Contains(fixture.Projects, p => p.Actions.Any(a => a.Description.Length > 150));
    }

    [Fact]
    public void Fixture_coversBothEndsOfThePriorityScoreRange()
    {
        var fixture = DemoDataFixture.Build(Now, new Dictionary<string, int>());
        var scores = fixture.Projects.Select(PriorityEngine.ComputeScore).ToList();

        Assert.Equal(100, scores.Max());
        Assert.Equal(0, scores.Min());
    }

    [Fact]
    public void SeededDemo_producesARecommendation_soTheLandingPageIsNeverEmpty()
    {
        // The Command Center hero is the first thing a reviewer sees. An empty
        // state there makes the whole app look broken.
        using var db = NewContext();
        DemoDataSeeder.SeedIfEmpty(db, DateTime.UtcNow);

        var projects = db.Projects
            .Include(p => p.Actions)
            .Include(p => p.Blockers).ThenInclude(b => b.BlockingProject)
            .AsNoTracking().ToList();

        var recommendation = PriorityEngine.GetRecommendation(projects);

        Assert.NotNull(recommendation.ProjectId);
        Assert.False(string.IsNullOrWhiteSpace(recommendation.ActionDescription));
    }

    [Fact]
    public void SeededDemo_persistsTheProjectToProjectBlockerLink()
    {
        using var db = NewContext();
        DemoDataSeeder.SeedIfEmpty(db, DateTime.UtcNow);

        var link = db.ProjectBlockers.Include(b => b.Project).Include(b => b.BlockingProject).Single();

        Assert.Equal("Book the anniversary trip", link.Project!.Name);
        Assert.Equal("Renew the expiring passport", link.BlockingProject!.Name);
    }
}
