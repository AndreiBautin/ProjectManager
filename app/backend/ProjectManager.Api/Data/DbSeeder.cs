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
