using Microsoft.EntityFrameworkCore;
using ProjectManager.Api.Data;
using ProjectManager.Api.Models;

namespace ProjectManager.Api.Demo;

/// <summary>
/// Writes <see cref="DemoDataFixture"/> into a database.
///
/// <para>
/// Barrier #3 of the demo-data guarantee: <b>seeding only ever fills empty
/// storage.</b> "Fill if empty" and "wipe and replace" are two separately named
/// operations - <see cref="SeedIfEmpty"/> and <see cref="ResetToDemo"/> - rather
/// than one method with a boolean. A call site therefore cannot ask for the
/// harmless one and receive the destructive one because an argument was wrong,
/// defaulted, or read in the wrong order. Both behaviours are covered by tests.
/// </para>
/// </summary>
public static class DemoDataSeeder
{
    /// <summary>
    /// Fills the database with demo data <b>only if it currently contains no
    /// projects</b>. If any project already exists this returns
    /// <see cref="DemoSeedOutcome.SkippedNotEmpty"/> and writes nothing at all.
    ///
    /// <para>
    /// This is the operation the application calls on startup. It is safe to run
    /// against any database, repeatedly, because it can only ever add to an
    /// empty one - it has no code path that deletes.
    /// </para>
    /// </summary>
    public static DemoSeedOutcome SeedIfEmpty(AppDbContext db, DateTime now)
    {
        if (db.Projects.Any()) return DemoSeedOutcome.SkippedNotEmpty;

        Insert(db, now);
        return DemoSeedOutcome.Seeded;
    }

    /// <summary>
    /// <b>Destructive.</b> Deletes every project (and, by cascade, every action
    /// and blocker link) and then writes a fresh copy of the demo fixture.
    ///
    /// <para>
    /// Never called on startup. It exists for the documented demo-reset path and
    /// for tests. It is deliberately verbose to type and impossible to reach by
    /// passing a wrong argument to the safe method.
    /// </para>
    /// </summary>
    public static DemoSeedOutcome ResetToDemo(AppDbContext db, DateTime now)
    {
        db.Projects.RemoveRange(db.Projects.Include(p => p.Actions).Include(p => p.Blockers));
        db.SaveChanges();

        Insert(db, now);
        return DemoSeedOutcome.Reset;
    }

    private static void Insert(AppDbContext db, DateTime now)
    {
        var categoryIds = db.Categories.ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);
        var fixture = DemoDataFixture.Build(now, categoryIds);

        db.Projects.AddRange(fixture.Projects);
        db.SaveChanges();

        // Blocker links are expressed by name in the fixture because IDs do not
        // exist until the projects above are saved.
        var idsByName = fixture.Projects.ToDictionary(p => p.Name, p => p.Id);
        foreach (var link in fixture.BlockerLinks)
        {
            if (!idsByName.TryGetValue(link.BlockedProjectName, out var blockedId)) continue;
            if (!idsByName.TryGetValue(link.BlockingProjectName, out var blockingId)) continue;

            db.ProjectBlockers.Add(new ProjectBlocker
            {
                ProjectId = blockedId,
                BlockingProjectId = blockingId,
            });
        }

        db.SaveChanges();
    }
}

public enum DemoSeedOutcome
{
    /// <summary>Demo data was written into an empty database.</summary>
    Seeded,

    /// <summary>The database already held projects; nothing was written or removed.</summary>
    SkippedNotEmpty,

    /// <summary>Existing data was deleted and replaced with a fresh fixture.</summary>
    Reset,
}
