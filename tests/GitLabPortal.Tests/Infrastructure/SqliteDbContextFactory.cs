using GitLabPortal.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GitLabPortal.Tests.Infrastructure;

/// <summary>
/// Backs <see cref="IDbContextFactory{TContext}"/> with a private in-memory SQLite database.
/// The connection stays open for the lifetime of the fixture so the schema survives between contexts.
/// </summary>
public sealed class SqliteDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public SqliteDbContextFactory(bool migrate = true)
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        if (migrate)
        {
            using var db = CreateDbContext();
            db.Database.Migrate();
        }
    }

    public AppDbContext CreateDbContext() => new(_options);

    public void Dispose() => _connection.Dispose();

    public void Seed(Action<AppDbContext> seed)
    {
        using var db = CreateDbContext();
        seed(db);
        db.SaveChanges();
    }
}
