using System.Security.Claims;
using GitLabPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace GitLabPortal.Services;

public record ProjectAccess(GitLabProject Project, bool CanView, bool CanRetry, bool CanRun, bool IsAdmin);

/// <summary>
/// Resolves the signed-in principal to a portal user and computes the per-project capabilities.
/// Admins implicitly have full access to every configured project.
/// </summary>
public class PortalAccessService(IDbContextFactory<AppDbContext> dbFactory)
{
    public static string? GetEmail(ClaimsPrincipal? principal) =>
        principal?.FindFirst(ClaimTypes.Email)?.Value
        ?? principal?.FindFirst("email")?.Value
        ?? principal?.FindFirst("preferred_username")?.Value
        ?? principal?.Identity?.Name;

    public async Task<AppUser?> GetUserAsync(ClaimsPrincipal? principal, CancellationToken ct = default)
    {
        var email = GetEmail(principal);
        if (string.IsNullOrWhiteSpace(email)) return null;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AppUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email.ToLowerInvariant() && u.IsActive, ct);
    }

    public async Task<List<ProjectAccess>> GetAccessibleProjectsAsync(ClaimsPrincipal? principal, CancellationToken ct = default)
    {
        var user = await GetUserAsync(principal, ct);
        if (user is null) return [];

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (user.Role == UserRole.Admin)
        {
            var all = await db.GitLabProjects.AsNoTracking()
                .Where(p => p.IsActive)
                .OrderBy(p => p.Name)
                .ToListAsync(ct);
            return [.. all.Select(p => new ProjectAccess(p, true, true, true, true))];
        }

        var granted = await db.ProjectPermissions.AsNoTracking()
            .Include(p => p.GitLabProject)
            .Where(p => p.AppUserId == user.Id && p.CanView && p.GitLabProject!.IsActive)
            .ToListAsync(ct);

        return [.. granted
            .Where(p => p.GitLabProject is not null)
            .OrderBy(p => p.GitLabProject!.Name)
            .Select(p => new ProjectAccess(p.GitLabProject!, p.CanView, p.CanRetry, p.CanRun, false))];
    }

    public async Task<ProjectAccess?> GetProjectAccessAsync(ClaimsPrincipal? principal, int projectId, CancellationToken ct = default)
    {
        var user = await GetUserAsync(principal, ct);
        if (user is null) return null;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var project = await db.GitLabProjects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return null;

        if (user.Role == UserRole.Admin)
            return new ProjectAccess(project, true, true, true, true);

        if (!project.IsActive) return null;

        var permission = await db.ProjectPermissions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.AppUserId == user.Id && p.GitLabProjectId == projectId, ct);

        if (permission is null || !permission.CanView) return null;

        return new ProjectAccess(project, permission.CanView, permission.CanRetry, permission.CanRun, false);
    }
}
