using GitLabPortal.Data;
using GitLabPortal.Services;
using GitLabPortal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace GitLabPortal.Tests;

public class DatabaseInitializerTests : IDisposable
{
    private readonly SqliteDbContextFactory _factory = new(migrate: false);

    public void Dispose() => _factory.Dispose();

    private DatabaseInitializer CreateInitializer(params string[] bootstrapAdmins)
    {
        var settings = bootstrapAdmins
            .Select((email, i) => new KeyValuePair<string, string?>($"Portal:BootstrapAdmins:{i}", email));

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new DatabaseInitializer(_factory, configuration, NullLogger<DatabaseInitializer>.Instance);
    }

    [Fact]
    public async Task InitializeAsync_applies_migrations()
    {
        await CreateInitializer().InitializeAsync();

        await using var db = _factory.CreateDbContext();
        Assert.Empty(await db.AppUsers.ToListAsync());
        Assert.Empty(await db.GitLabProjects.ToListAsync());
    }

    [Fact]
    public async Task Bootstrap_admins_are_created_lowercased()
    {
        await CreateInitializer("Admin@Example.com", "  ", "second@example.com").InitializeAsync();

        await using var db = _factory.CreateDbContext();
        var users = await db.AppUsers.OrderBy(u => u.Email).ToListAsync();

        Assert.Equal(["admin@example.com", "second@example.com"], users.Select(u => u.Email));
        Assert.All(users, u =>
        {
            Assert.Equal(UserRole.Admin, u.Role);
            Assert.True(u.IsActive);
        });
    }

    [Fact]
    public async Task An_existing_user_is_promoted_and_reactivated()
    {
        await CreateInitializer().InitializeAsync();
        _factory.Seed(db => db.AppUsers.Add(new AppUser
        {
            Email = "admin@example.com",
            Role = UserRole.Operator,
            IsActive = false
        }));

        await CreateInitializer("admin@example.com").InitializeAsync();

        await using var db = _factory.CreateDbContext();
        var user = Assert.Single(await db.AppUsers.ToListAsync());
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.True(user.IsActive);
    }

    [Fact]
    public async Task Running_twice_does_not_duplicate_admins()
    {
        await CreateInitializer("admin@example.com").InitializeAsync();
        await CreateInitializer("admin@example.com").InitializeAsync();

        await using var db = _factory.CreateDbContext();
        Assert.Single(await db.AppUsers.ToListAsync());
    }
}
