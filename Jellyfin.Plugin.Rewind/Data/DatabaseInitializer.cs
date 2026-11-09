using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Rewind.Data;

/// <summary>
/// Creates the plugin-owned database when the server starts.
/// </summary>
/// <remarks>
/// <c>EnsureCreated</c> only builds the schema for a database that does not exist yet. Columns
/// or tables added in later versions must be back-filled here with idempotent
/// <c>CREATE ... IF NOT EXISTS</c> / <c>ALTER TABLE ... ADD COLUMN</c> statements.
/// </remarks>
public class DatabaseInitializer : IHostedService
{
    // Columns added after the first release, in order. SQL is composed only of these literals.
    private static readonly (string Name, string Sql)[] _addedColumns =
    [
        ("DiscNumber", "ALTER TABLE \"Plays\" ADD COLUMN \"DiscNumber\" INTEGER NULL;"),
        ("Genres", "ALTER TABLE \"Plays\" ADD COLUMN \"Genres\" TEXT NULL;"),
        ("SnapshotVersion", "ALTER TABLE \"Plays\" ADD COLUMN \"SnapshotVersion\" INTEGER NOT NULL DEFAULT 0;"),
        ("IsPaused", "ALTER TABLE \"Plays\" ADD COLUMN \"IsPaused\" INTEGER NOT NULL DEFAULT 0;"),
    ];

    private readonly IDbContextFactory<RewindDbContext> _contextFactory;
    private readonly ILogger<DatabaseInitializer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseInitializer"/> class.
    /// </summary>
    /// <param name="contextFactory">The database context factory.</param>
    /// <param name="logger">The logger.</param>
    public DatabaseInitializer(IDbContextFactory<RewindDbContext> contextFactory, ILogger<DatabaseInitializer> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // An exception thrown from a hosted service would abort the whole Jellyfin host; a
        // plugin must never take down the server.
        try
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rewind: unable to initialize the database; play history will not be recorded.");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Creates the schema and switches the database to write-ahead logging, so API reads never
    /// block the playback writes.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the database is ready.</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);

            var existing = await ColumnsAsync(context, cancellationToken).ConfigureAwait(false);
            foreach (var (name, sql) in _addedColumns)
            {
                if (!existing.Contains(name))
                {
                    _logger.LogInformation("Rewind: adding column {Column}.", name);
                    await context.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private static async Task<HashSet<string>> ColumnsAsync(RewindDbContext context, CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = "SELECT name FROM pragma_table_info('Plays');";
                var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        columns.Add(reader.GetString(0));
                    }
                }
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }

        return columns;
    }
}
