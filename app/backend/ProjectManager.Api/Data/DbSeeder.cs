using Microsoft.EntityFrameworkCore;
using ProjectManager.Api.Models;

namespace ProjectManager.Api.Data;

public static class DbSeeder
{
    private static readonly string[] DefaultCategories =
    {
        "Home", "Career", "Finance", "Personal", "Relationships", "Hobbies"
    };

    public static void Seed(AppDbContext db)
    {
        db.Database.EnsureCreated();
        ApplySchemaPatches(db);

        if (!db.Categories.Any())
        {
            foreach (var name in DefaultCategories)
            {
                db.Categories.Add(new Category { Name = name });
            }
            db.SaveChanges();
        }
    }

    // This project uses EnsureCreated() instead of full EF Core migrations for
    // simplicity, which means it won't alter a database that already exists.
    // When a field is added to the model after people already have data,
    // patch it in by hand here (idempotent - checked and applied every
    // startup) so existing projectmanager.db files don't break.
    private static void ApplySchemaPatches(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        AddColumnIfMissing(connection, "Actions", "AvailableFrom", "TEXT");
        AddColumnIfMissing(connection, "Projects", "Deadline", "TEXT");
        CreateProjectBlockersTableIfMissing(connection);
        DropColumnIfPresent(connection, "Projects", "Progress");
    }

    // EnsureCreated() only creates tables when the database doesn't exist yet at
    // all, so a brand new table added after people already have data needs to be
    // created by hand here too, not just new columns on existing tables.
    private static void CreateProjectBlockersTableIfMissing(System.Data.Common.DbConnection connection)
    {
        using var create = connection.CreateCommand();
        create.CommandText = @"
            CREATE TABLE IF NOT EXISTS ProjectBlockers (
                Id INTEGER NOT NULL CONSTRAINT PK_ProjectBlockers PRIMARY KEY AUTOINCREMENT,
                ProjectId INTEGER NOT NULL,
                BlockingProjectId INTEGER NOT NULL,
                CONSTRAINT FK_ProjectBlockers_Projects_ProjectId FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE CASCADE,
                CONSTRAINT FK_ProjectBlockers_Projects_BlockingProjectId FOREIGN KEY (BlockingProjectId) REFERENCES Projects (Id) ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_ProjectBlockers_ProjectId_BlockingProjectId ON ProjectBlockers (ProjectId, BlockingProjectId);
        ";
        create.ExecuteNonQuery();
    }

    // Progress used to be a hand-managed column; it's now derived from how many
    // Actions are Done (see PriorityEngine.ComputeProgress). Drop the stale
    // column on existing databases so it isn't left as an unused NOT NULL field
    // that would reject inserts now that the model no longer maps it. SQLite has
    // supported ALTER TABLE DROP COLUMN since 3.35 (well below the version the
    // EF Core Sqlite provider bundles); guarded anyway so a failure here can
    // never block startup.
    private static void DropColumnIfPresent(
        System.Data.Common.DbConnection connection, string table, string column)
    {
        var present = false;
        using (var check = connection.CreateCommand())
        {
            check.CommandText = $"PRAGMA table_info({table});";
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader["name"]?.ToString(), column, StringComparison.OrdinalIgnoreCase))
                {
                    present = true;
                    break;
                }
            }
        }

        if (!present) return;

        try
        {
            using var drop = connection.CreateCommand();
            drop.CommandText = $"ALTER TABLE {table} DROP COLUMN {column};";
            drop.ExecuteNonQuery();
        }
        catch
        {
            // Leaving the column in place is harmless - it just sits unused.
        }
    }

    private static void AddColumnIfMissing(
        System.Data.Common.DbConnection connection, string table, string column, string sqliteType)
    {
        using (var check = connection.CreateCommand())
        {
            check.CommandText = $"PRAGMA table_info({table});";
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader["name"]?.ToString(), column, StringComparison.OrdinalIgnoreCase))
                {
                    return; // column already present
                }
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {sqliteType} NULL;";
        alter.ExecuteNonQuery();
    }
}
