using Microsoft.EntityFrameworkCore;
using ProjectManager.Api.Models;

namespace ProjectManager.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ActionItem> Actions => Set<ActionItem>();
    public DbSet<ProjectBlocker> ProjectBlockers => Set<ProjectBlocker>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasIndex(c => c.Name).IsUnique();
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.Property(p => p.Status).HasConversion<string>();
            entity.HasIndex(p => p.Status);
            entity.HasIndex(p => p.CategoryId);

            entity.HasOne(p => p.Category)
                  .WithMany(c => c.Projects)
                  .HasForeignKey(p => p.CategoryId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ActionItem>(entity =>
        {
            entity.Property(a => a.Status).HasConversion<string>();
            entity.HasIndex(a => new { a.ProjectId, a.Order });

            entity.HasOne(a => a.Project)
                  .WithMany(p => p.Actions)
                  .HasForeignKey(a => a.ProjectId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProjectBlocker>(entity =>
        {
            entity.HasIndex(b => new { b.ProjectId, b.BlockingProjectId }).IsUnique();

            entity.HasOne(b => b.Project)
                  .WithMany(p => p.Blockers)
                  .HasForeignKey(b => b.ProjectId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(b => b.BlockingProject)
                  .WithMany()
                  .HasForeignKey(b => b.BlockingProjectId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
