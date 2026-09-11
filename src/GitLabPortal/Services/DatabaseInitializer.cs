using GitLabPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace GitLabPortal.Services;

/// <summary>Applies migrations at startup and promotes the configured bootstrap admins.</summary>
public class DatabaseInitializer(
    IDbContextFactory<AppDbContext> dbFactory,
    IConfiguration configuration,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);

        var admins = configuration.GetSection("Portal:BootstrapAdmins").Get<string[]>() ?? [];
        foreach (var raw in admins)
        {
            var email = raw?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email)) continue;

            var existing = await db.AppUsers.FirstOrDefaultAsync(u => u.Email == email, ct);
            if (existing is null)
            {
                db.AppUsers.Add(new AppUser { Email = email, Role = UserRole.Admin, IsActive = true });
                logger.LogInformation("Bootstrapping admin account {Email}.", email);
            }
            else if (existing.Role != UserRole.Admin || !existing.IsActive)
            {
                existing.Role = UserRole.Admin;
                existing.IsActive = true;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
