using System.Security.Claims;
using GitLabPortal.Data;
using GitLabPortal.Services;
using GitLabPortal.Tests.Infrastructure;

namespace GitLabPortal.Tests;

public class PortalClaimsTransformationTests : IDisposable
{
    private readonly SqliteDbContextFactory _factory = new();
    private readonly PortalClaimsTransformation _transformation;

    public PortalClaimsTransformationTests() => _transformation = new PortalClaimsTransformation(_factory);

    public void Dispose() => _factory.Dispose();

    private static ClaimsPrincipal Authenticated(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "oidc"));

    [Fact]
    public async Task Anonymous_principals_are_returned_untouched()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await _transformation.TransformAsync(principal);

        Assert.Same(principal, result);
        Assert.False(result.HasClaim(c => c.Type == PortalClaimsTransformation.PortalUserIdClaim));
    }

    [Fact]
    public async Task A_known_user_receives_portal_id_and_role_claims()
    {
        var userId = 0;
        _factory.Seed(db =>
        {
            var user = new AppUser { Email = "admin@example.com", Role = UserRole.Admin };
            db.AppUsers.Add(user);
            db.SaveChanges();
            userId = user.Id;
        });

        var result = await _transformation.TransformAsync(
            Authenticated(new Claim(ClaimTypes.Email, "Admin@Example.com")));

        Assert.Equal(userId.ToString(), result.FindFirst(PortalClaimsTransformation.PortalUserIdClaim)?.Value);
        Assert.True(result.IsInRole(nameof(UserRole.Admin)));
    }

    [Fact]
    public async Task An_unknown_user_gets_no_portal_claims()
    {
        var result = await _transformation.TransformAsync(
            Authenticated(new Claim(ClaimTypes.Email, "stranger@example.com")));

        Assert.False(result.HasClaim(c => c.Type == PortalClaimsTransformation.PortalUserIdClaim));
    }

    [Fact]
    public async Task A_deactivated_user_gets_no_portal_claims()
    {
        _factory.Seed(db => db.AppUsers.Add(new AppUser { Email = "gone@example.com", IsActive = false, Role = UserRole.Admin }));

        var result = await _transformation.TransformAsync(
            Authenticated(new Claim(ClaimTypes.Email, "gone@example.com")));

        Assert.False(result.HasClaim(c => c.Type == PortalClaimsTransformation.PortalUserIdClaim));
        Assert.False(result.IsInRole(nameof(UserRole.Admin)));
    }

    [Fact]
    public async Task The_email_claim_is_added_when_only_preferred_username_was_issued()
    {
        _factory.Seed(db => db.AppUsers.Add(new AppUser { Email = "op@example.com" }));

        var result = await _transformation.TransformAsync(
            Authenticated(new Claim("preferred_username", "op@example.com")));

        Assert.Equal("op@example.com", result.FindFirst(ClaimTypes.Email)?.Value);
    }

    [Fact]
    public async Task Transforming_twice_does_not_duplicate_claims()
    {
        _factory.Seed(db => db.AppUsers.Add(new AppUser { Email = "op@example.com" }));
        var principal = Authenticated(new Claim(ClaimTypes.Email, "op@example.com"));

        await _transformation.TransformAsync(principal);
        var result = await _transformation.TransformAsync(principal);

        Assert.Single(result.FindAll(PortalClaimsTransformation.PortalUserIdClaim));
    }
}
