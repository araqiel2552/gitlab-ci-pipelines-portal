using System.Security.Claims;
using GitLabPortal.Data;
using GitLabPortal.Services;
using GitLabPortal.Tests.Infrastructure;

namespace GitLabPortal.Tests;

public class PortalAccessServiceTests : IDisposable
{
    private readonly SqliteDbContextFactory _factory = new();
    private readonly PortalAccessService _service;

    public PortalAccessServiceTests() => _service = new PortalAccessService(_factory);

    public void Dispose() => _factory.Dispose();

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Test"));

    private static ClaimsPrincipal Email(string email) =>
        Principal(new Claim(ClaimTypes.Email, email));

    private static GitLabProject NewProject(string name, bool isActive = true) => new()
    {
        Name = name,
        GitLabUrl = "https://gitlab.example.com",
        ProjectId = name,
        AccessTokenProtected = "enc:token",
        IsActive = isActive
    };

    [Fact]
    public void GetEmail_prefers_the_email_claim()
    {
        var principal = Principal(
            new Claim(ClaimTypes.Email, "primary@example.com"),
            new Claim("preferred_username", "other@example.com"));

        Assert.Equal("primary@example.com", PortalAccessService.GetEmail(principal));
    }

    [Fact]
    public void GetEmail_falls_back_through_oidc_claim_names()
    {
        Assert.Equal("a@example.com", PortalAccessService.GetEmail(Principal(new Claim("email", "a@example.com"))));
        Assert.Equal("b@example.com", PortalAccessService.GetEmail(Principal(new Claim("preferred_username", "b@example.com"))));
        Assert.Equal("c@example.com", PortalAccessService.GetEmail(Principal(new Claim(ClaimTypes.Name, "c@example.com"))));
    }

    [Fact]
    public void GetEmail_returns_null_without_a_principal_or_claims()
    {
        Assert.Null(PortalAccessService.GetEmail(null));
        Assert.Null(PortalAccessService.GetEmail(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public async Task GetUserAsync_resolves_an_active_user()
    {
        _factory.Seed(db => db.AppUsers.Add(new AppUser { Email = "op@example.com", Role = UserRole.Operator }));

        var user = await _service.GetUserAsync(Email("op@example.com"));

        Assert.NotNull(user);
        Assert.Equal(UserRole.Operator, user.Role);
    }

    [Fact]
    public async Task GetUserAsync_ignores_deactivated_users()
    {
        _factory.Seed(db => db.AppUsers.Add(new AppUser { Email = "gone@example.com", IsActive = false }));

        Assert.Null(await _service.GetUserAsync(Email("gone@example.com")));
    }

    [Fact]
    public async Task GetUserAsync_returns_null_for_an_unknown_email()
        => Assert.Null(await _service.GetUserAsync(Email("stranger@example.com")));

    [Fact]
    public async Task Admins_see_every_active_project_with_full_capabilities()
    {
        _factory.Seed(db =>
        {
            db.AppUsers.Add(new AppUser { Email = "admin@example.com", Role = UserRole.Admin });
            db.GitLabProjects.AddRange(NewProject("beta"), NewProject("alpha"), NewProject("archived", isActive: false));
        });

        var access = await _service.GetAccessibleProjectsAsync(Email("admin@example.com"));

        Assert.Equal(["alpha", "beta"], access.Select(a => a.Project.Name));
        Assert.All(access, a =>
        {
            Assert.True(a.IsAdmin);
            Assert.True(a.CanView);
            Assert.True(a.CanRetry);
            Assert.True(a.CanRun);
        });
    }

    [Fact]
    public async Task Operators_only_see_projects_they_were_granted_view_on()
    {
        _factory.Seed(db =>
        {
            var user = new AppUser { Email = "op@example.com", Role = UserRole.Operator };
            var granted = NewProject("granted");
            var denied = NewProject("denied");
            var hidden = NewProject("hidden");
            db.AddRange(user, granted, denied, hidden);
            db.SaveChanges();

            db.ProjectPermissions.AddRange(
                new ProjectPermission { AppUserId = user.Id, GitLabProjectId = granted.Id, CanView = true, CanRetry = true },
                new ProjectPermission { AppUserId = user.Id, GitLabProjectId = denied.Id, CanView = false });
        });

        var access = await _service.GetAccessibleProjectsAsync(Email("op@example.com"));

        var only = Assert.Single(access);
        Assert.Equal("granted", only.Project.Name);
        Assert.True(only.CanRetry);
        Assert.False(only.CanRun);
        Assert.False(only.IsAdmin);
    }

    [Fact]
    public async Task Operators_do_not_see_permissions_on_deactivated_projects()
    {
        _factory.Seed(db =>
        {
            var user = new AppUser { Email = "op@example.com" };
            var project = NewProject("archived", isActive: false);
            db.AddRange(user, project);
            db.SaveChanges();
            db.ProjectPermissions.Add(new ProjectPermission { AppUserId = user.Id, GitLabProjectId = project.Id, CanView = true });
        });

        Assert.Empty(await _service.GetAccessibleProjectsAsync(Email("op@example.com")));
    }

    [Fact]
    public async Task Unknown_users_get_no_projects()
        => Assert.Empty(await _service.GetAccessibleProjectsAsync(Email("stranger@example.com")));

    [Fact]
    public async Task GetProjectAccessAsync_grants_admins_access_even_to_inactive_projects()
    {
        var projectId = 0;
        _factory.Seed(db =>
        {
            db.AppUsers.Add(new AppUser { Email = "admin@example.com", Role = UserRole.Admin });
            var project = NewProject("archived", isActive: false);
            db.GitLabProjects.Add(project);
            db.SaveChanges();
            projectId = project.Id;
        });

        var access = await _service.GetProjectAccessAsync(Email("admin@example.com"), projectId);

        Assert.NotNull(access);
        Assert.True(access.IsAdmin);
        Assert.True(access.CanRun);
    }

    [Fact]
    public async Task GetProjectAccessAsync_returns_the_granted_capabilities_for_an_operator()
    {
        var projectId = 0;
        _factory.Seed(db =>
        {
            var user = new AppUser { Email = "op@example.com" };
            var project = NewProject("granted");
            db.AddRange(user, project);
            db.SaveChanges();
            projectId = project.Id;
            db.ProjectPermissions.Add(new ProjectPermission
            {
                AppUserId = user.Id,
                GitLabProjectId = project.Id,
                CanView = true,
                CanRetry = false,
                CanRun = true
            });
        });

        var access = await _service.GetProjectAccessAsync(Email("op@example.com"), projectId);

        Assert.NotNull(access);
        Assert.True(access.CanView);
        Assert.False(access.CanRetry);
        Assert.True(access.CanRun);
        Assert.False(access.IsAdmin);
    }

    [Fact]
    public async Task GetProjectAccessAsync_denies_an_operator_without_a_permission_row()
    {
        var projectId = 0;
        _factory.Seed(db =>
        {
            db.AppUsers.Add(new AppUser { Email = "op@example.com" });
            var project = NewProject("granted");
            db.GitLabProjects.Add(project);
            db.SaveChanges();
            projectId = project.Id;
        });

        Assert.Null(await _service.GetProjectAccessAsync(Email("op@example.com"), projectId));
    }

    [Fact]
    public async Task GetProjectAccessAsync_returns_null_for_an_unknown_project()
    {
        _factory.Seed(db => db.AppUsers.Add(new AppUser { Email = "admin@example.com", Role = UserRole.Admin }));

        Assert.Null(await _service.GetProjectAccessAsync(Email("admin@example.com"), 9999));
    }
}
