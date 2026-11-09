using System;
using Jellyfin.Plugin.Rewind.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.Rewind.Tests;

/// <summary>
/// A context factory over one in-memory SQLite connection with the schema created.
/// </summary>
public sealed class TestDatabase : IDbContextFactory<RewindDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var context = CreateDbContext();
        context.Database.EnsureCreated();
    }

    public RewindDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RewindDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new RewindDbContext(options);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
