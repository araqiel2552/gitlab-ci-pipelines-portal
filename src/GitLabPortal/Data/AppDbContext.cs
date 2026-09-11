using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GitLabPortal.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<GitLabProject> GitLabProjects => Set<GitLabProject>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<ProjectPermission> ProjectPermissions => Set<ProjectPermission>();
    public DbSet<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey> DataProtectionKeys => Set<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<GitLabProject>()
            .HasIndex(p => new { p.GitLabUrl, p.ProjectId })
            .IsUnique();

        modelBuilder.Entity<ProjectPermission>()
            .HasIndex(p => new { p.AppUserId, p.GitLabProjectId })
            .IsUnique();

        modelBuilder.Entity<ProjectPermission>()
            .HasOne(p => p.AppUser)
            .WithMany(u => u.Permissions)
            .HasForeignKey(p => p.AppUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProjectPermission>()
            .HasOne(p => p.GitLabProject)
            .WithMany(p => p.Permissions)
            .HasForeignKey(p => p.GitLabProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
