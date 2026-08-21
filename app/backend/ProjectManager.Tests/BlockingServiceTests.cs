using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManager.Api.Data;
using ProjectManager.Api.Models;
using ProjectManager.Api.Services;

namespace ProjectManager.Tests;

/// <summary>
/// BlockingService is the one piece of domain logic that needs a database, and
/// the one place a bug would be genuinely hard to spot by inspection: a missed
/// cycle check produces a dependency graph that never resolves.
///
/// <para>
/// Uses real in-memory SQLite rather than the EF in-memory provider, so cascade
/// deletes and unique indexes behave the way they will in production.
/// </para>
/// </summary>
public class BlockingServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public BlockingServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using var db = new AppDbContext(_options);
        DbSeeder.Seed(db);
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewContext() => new(_options);

    private async Task<int> AddProject(string name, ProjectStatus status = ProjectStatus.Active)
    {
        await using var db = NewContext();
        var project = new Project { Name = name, Status = status };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project.Id;
    }

    private async Task Link(int blockedId, int blockingId)
    {
        await using var db = NewContext();
        db.ProjectBlockers.Add(new ProjectBlocker { ProjectId = blockedId, BlockingProjectId = blockingId });
        await db.SaveChangesAsync();
    }

    // ---- Validation --------------------------------------------------------

    [Fact]
    public async Task Validate_acceptsAnEmptyBlockerSet()
    {
        await using var db = NewContext();
        Assert.Null(await new BlockingService(db).ValidateBlockersAsync(1, Array.Empty<int>()));
    }

    [Fact]
    public async Task Validate_rejectsAProjectBlockingItself()
    {
        var id = await AddProject("self");
        await using var db = NewContext();

        var error = await new BlockingService(db).ValidateBlockersAsync(id, new[] { id });

        Assert.Contains("cannot block itself", error);
    }

    [Fact]
    public async Task Validate_rejectsAnUnknownProjectId()
    {
        var id = await AddProject("real");
        await using var db = NewContext();

        var error = await new BlockingService(db).ValidateBlockersAsync(id, new[] { 999_999 });

        Assert.Contains("Unknown project id", error);
    }

    [Fact]
    public async Task Validate_rejectsADirectTwoProjectCycle()
    {
        var a = await AddProject("a");
        var b = await AddProject("b");
        await Link(a, b); // a waits on b

        await using var db = NewContext();
        var error = await new BlockingService(db).ValidateBlockersAsync(b, new[] { a }); // b waits on a

        Assert.Contains("circular dependency", error);
    }

    [Fact]
    public async Task Validate_rejectsALongerTransitiveCycle()
    {
        // a -> b -> c -> d, then asking d to wait on a closes the loop.
        var a = await AddProject("a");
        var b = await AddProject("b");
        var c = await AddProject("c");
        var d = await AddProject("d");
        await Link(a, b);
        await Link(b, c);
        await Link(c, d);

        await using var db = NewContext();
        var error = await new BlockingService(db).ValidateBlockersAsync(d, new[] { a });

        Assert.Contains("circular dependency", error);
    }

    [Fact]
    public async Task Validate_allowsADiamondWhichIsNotACycle()
    {
        // a and b both wait on c. Two paths to the same node is a legitimate
        // shape - rejecting it would be an over-eager cycle check.
        var a = await AddProject("a");
        var b = await AddProject("b");
        var c = await AddProject("c");
        await Link(a, c);

        await using var db = NewContext();
        Assert.Null(await new BlockingService(db).ValidateBlockersAsync(b, new[] { c }));
    }

    // ---- Sync ---------------------------------------------------------------

    [Fact]
    public async Task SyncBlockers_addsRemovesAndLeavesUnchangedLinksAlone()
    {
        var target = await AddProject("target");
        var keep = await AddProject("keep");
        var drop = await AddProject("drop");
        var add = await AddProject("add");
        await Link(target, keep);
        await Link(target, drop);

        await using (var db = NewContext())
        {
            var project = await db.Projects.Include(p => p.Blockers).FirstAsync(p => p.Id == target);
            new BlockingService(db).SyncBlockers(project, new List<int> { keep, add });
            await db.SaveChangesAsync();
        }

        await using var verify = NewContext();
        var ids = verify.ProjectBlockers.Where(b => b.ProjectId == target)
            .Select(b => b.BlockingProjectId).OrderBy(x => x).ToList();

        Assert.Equal(new[] { keep, add }.OrderBy(x => x), ids);
    }

    [Fact]
    public async Task SyncBlockers_deduplicatesRepeatedIds()
    {
        var target = await AddProject("target");
        var blocker = await AddProject("blocker");

        await using (var db = NewContext())
        {
            var project = await db.Projects.Include(p => p.Blockers).FirstAsync(p => p.Id == target);
            new BlockingService(db).SyncBlockers(project, new List<int> { blocker, blocker, blocker });
            await db.SaveChangesAsync();
        }

        await using var verify = NewContext();
        Assert.Single(verify.ProjectBlockers.Where(b => b.ProjectId == target));
    }

    // ---- Recompute ------------------------------------------------------------

    [Fact]
    public async Task RecomputeDependents_unblocksAProjectOnceItsBlockerCompletes()
    {
        var blocked = await AddProject("blocked", ProjectStatus.Blocked);
        var blocker = await AddProject("blocker");
        await Link(blocked, blocker);

        await using (var db = NewContext())
        {
            (await db.Projects.FindAsync(blocker))!.Status = ProjectStatus.Completed;
            await db.SaveChangesAsync();
            await new BlockingService(db).RecomputeDependentsAsync(blocker);
        }

        await using var verify = NewContext();
        Assert.Equal(ProjectStatus.Active, (await verify.Projects.FindAsync(blocked))!.Status);
    }

    [Fact]
    public async Task RecomputeDependents_keepsAProjectBlocked_whileAnyBlockerIsStillOpen()
    {
        var blocked = await AddProject("blocked", ProjectStatus.Blocked);
        var first = await AddProject("first");
        var second = await AddProject("second");
        await Link(blocked, first);
        await Link(blocked, second);

        await using (var db = NewContext())
        {
            (await db.Projects.FindAsync(first))!.Status = ProjectStatus.Completed;
            await db.SaveChangesAsync();
            await new BlockingService(db).RecomputeDependentsAsync(first);
        }

        await using var verify = NewContext();
        Assert.Equal(ProjectStatus.Blocked, (await verify.Projects.FindAsync(blocked))!.Status);
    }

    [Fact]
    public async Task RecomputeDependents_doesNotDisturbAPausedProject()
    {
        // Paused is an explicit user choice. Auto-unblocking must not quietly
        // drag something back into the active list that was set aside on purpose.
        var paused = await AddProject("paused", ProjectStatus.Paused);
        var blocker = await AddProject("blocker");
        await Link(paused, blocker);

        await using (var db = NewContext())
        {
            (await db.Projects.FindAsync(blocker))!.Status = ProjectStatus.Completed;
            await db.SaveChangesAsync();
            await new BlockingService(db).RecomputeDependentsAsync(blocker);
        }

        await using var verify = NewContext();
        Assert.Equal(ProjectStatus.Paused, (await verify.Projects.FindAsync(paused))!.Status);
    }

    [Fact]
    public async Task GetDependentIds_findsProjectsWaitingOnTheGivenOne()
    {
        var blocker = await AddProject("blocker");
        var one = await AddProject("one");
        var two = await AddProject("two");
        await Link(one, blocker);
        await Link(two, blocker);

        await using var db = NewContext();
        var dependents = await new BlockingService(db).GetDependentIdsAsync(blocker);

        Assert.Equal(new[] { one, two }.OrderBy(x => x), dependents.OrderBy(x => x));
    }

    [Fact]
    public async Task DeletingABlockerCascadesTheJoinRow()
    {
        // Confirms the cascade the controller relies on when it captures
        // dependents *before* deleting - because afterwards they are gone.
        var blocked = await AddProject("blocked", ProjectStatus.Blocked);
        var blocker = await AddProject("blocker");
        await Link(blocked, blocker);

        await using (var db = NewContext())
        {
            db.Projects.Remove((await db.Projects.FindAsync(blocker))!);
            await db.SaveChangesAsync();
        }

        await using var verify = NewContext();
        Assert.Empty(verify.ProjectBlockers);
    }
}
