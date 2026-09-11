using System.Security.Claims;
using GitLabPortal.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace GitLabPortal.Services;

/// <summary>Projects the portal role stored in SQLite onto the OIDC principal.</summary>
public class PortalClaimsTransformation(IDbContextFactory<AppDbContext> dbFactory) : IClaimsTransformation
{
    public const string PortalUserIdClaim = "portal:user_id";

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true) return principal;
        if (principal.HasClaim(c => c.Type == PortalUserIdClaim)) return principal;

        var email = PortalAccessService.GetEmail(principal)?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email)) return principal;

        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || !user.IsActive) return principal;

        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim(PortalUserIdClaim, user.Id.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Role, user.Role.ToString()));
        if (!principal.HasClaim(c => c.Type == ClaimTypes.Email))
            identity.AddClaim(new Claim(ClaimTypes.Email, user.Email));

        principal.AddIdentity(identity);
        return principal;
    }
}
