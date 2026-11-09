using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Rewind.Data;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.Rewind.Services;

/// <summary>
/// The SQLite-backed <see cref="IHistoryStore"/>.
/// </summary>
public sealed class HistoryStore : IHistoryStore, IDisposable
{
    private const string LikeEscape = "\\";
    private const int RewriteBatch = 500;

    private readonly IDbContextFactory<RewindDbContext> _contextFactory;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private long? _lastSeq;

    /// <summary>
    /// Initializes a new instance of the <see cref="HistoryStore"/> class.
    /// </summary>
    /// <param name="contextFactory">The database context factory.</param>
    /// <param name="time">The clock.</param>
    public HistoryStore(IDbContextFactory<RewindDbContext> contextFactory, TimeProvider time)
    {
        _contextFactory = contextFactory;
        _time = time;
    }

    /// <inheritdoc />
    public void Dispose() => _writeLock.Dispose();

    /// <inheritdoc />
    public async Task InsertAsync(PlayRecord play, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(play);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                await StampAsync(context, play, cancellationToken).ConfigureAwait(false);
                context.Plays.Add(play);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpdateAsync(PlayRecord play, bool announce, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(play);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                if (announce)
                {
                    await StampAsync(context, play, cancellationToken).ConfigureAwait(false);
                }

                context.Plays.Update(play);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<PlayRecord?> FindBySessionKeyAsync(string sessionKey, CancellationToken cancellationToken)
    {
        var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            return await context.Plays.AsNoTracking()
                .Where(play => play.SessionKey == sessionKey && !play.IsDeleted)
                .OrderByDescending(play => play.StartedAt)
                .ThenByDescending(play => play.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<PlayRecord?> FindFalseStartAsync(Guid userId, Guid itemId, string? deviceId, DateTime endedSince, long maxPlayedTicks, CancellationToken cancellationToken)
    {
        var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            return await context.Plays.AsNoTracking()
                .Where(play => play.UserId == userId
                    && play.ItemId == itemId
                    && play.DeviceId == deviceId
                    && play.Source == PlaySource.Playback
                    && play.Status != PlayStatus.Active
                    && !play.IsDeleted
                    && play.EndedAt >= endedSince
                    && play.PlayedTicks < maxPlayedTicks

                    // A play that counted is never a false start, however short the track:
                    // replaying a 5-second song must leave two plays.
                    && !play.Scrobbled
                    && !play.PlayedToCompletion)
                .OrderByDescending(play => play.EndedAt)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlayRecord>> ActiveOnDeviceAsync(Guid userId, string deviceId, CancellationToken cancellationToken)
    {
        var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            return await context.Plays.AsNoTracking()
                .Where(play => play.UserId == userId && play.DeviceId == deviceId && play.Status == PlayStatus.Active && !play.IsDeleted)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlayRecord>> StaleAsync(DateTime cutoff, CancellationToken cancellationToken)
    {
        var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            return await context.Plays.AsNoTracking()
                .Where(play => play.Status == PlayStatus.Active && play.LastSeenAt < cutoff && !play.IsDeleted)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<bool> HasRecentScrobbleAsync(Guid userId, Guid itemId, DateTime since, CancellationToken cancellationToken)
    {
        var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            return await context.Plays.AsNoTracking()
                .AnyAsync(
                    play => play.UserId == userId && play.ItemId == itemId && play.Scrobbled && !play.IsDeleted && play.EndedAt >= since,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<PlayRecord> Items, bool HasMore)> QueryAsync(HistoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var plays = Filter(context.Plays.AsNoTracking(), query);

            // string.Compare translates to SQLite's BINARY (ordinal) comparison of the ids.
#pragma warning disable CA1309
            if (query.After is { } after)
            {
                var at = new DateTime(after.StartedTicks, DateTimeKind.Utc);
                var id = after.Id;
                plays = query.Descending
                    ? plays.Where(play => play.StartedAt < at || (play.StartedAt == at && string.Compare(play.Id, id) < 0))
                    : plays.Where(play => play.StartedAt > at || (play.StartedAt == at && string.Compare(play.Id, id) > 0));
            }
#pragma warning restore CA1309

            plays = query.Descending
                ? plays.OrderByDescending(play => play.StartedAt).ThenByDescending(play => play.Id)
                : plays.OrderBy(play => play.StartedAt).ThenBy(play => play.Id);

            var rows = await plays.Take(query.Limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
            var hasMore = rows.Count > query.Limit;
            if (hasMore)
            {
                rows.RemoveAt(rows.Count - 1);
            }

            return (rows, hasMore);
        }
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<PlayRecord> Items, bool HasMore)> ChangesAsync(Guid userId, long since, int limit, IReadOnlyCollection<string>? types, CancellationToken cancellationToken)
    {
        var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var plays = context.Plays.AsNoTracking().Where(play => play.UserId == userId && play.Seq > since);
            if (types is { Count: > 0 })
            {
                plays = plays.Where(play => types.Contains(play.ItemType));
            }

            var rows = await plays.OrderBy(play => play.Seq).Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
            var hasMore = rows.Count > limit;
            if (hasMore)
            {
                rows.RemoveAt(rows.Count - 1);
            }

            return (rows, hasMore);
        }
    }

    /// <inheritdoc />
    public async Task<PlayRecord?> GetAsync(Guid userId, string id, CancellationToken cancellationToken)
    {
        var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            return await context.Plays.AsNoTracking()
                .FirstOrDefaultAsync(play => play.Id == id && play.UserId == userId && !play.IsDeleted, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid userId, string id, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                var play = await context.Plays
                    .FirstOrDefaultAsync(row => row.Id == id && row.UserId == userId && !row.IsDeleted, cancellationToken)
                    .ConfigureAwait(false);
                if (play is null)
                {
                    return false;
                }

                play.IsDeleted = true;
                await StampAsync(context, play, cancellationToken).ConfigureAwait(false);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<int> PurgeUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                return await context.Plays.Where(play => play.UserId == userId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public Task<int> ReevaluateAsync(Func<PlayRecord, bool> evaluate, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evaluate);

        return RewriteAsync(
            plays => plays.Where(play => play.Status != PlayStatus.Active && !play.IsDeleted),
            play =>
            {
                var scrobbled = evaluate(play);
                if (scrobbled == play.Scrobbled)
                {
                    return false;
                }

                play.Scrobbled = scrobbled;
                return true;
            },
            progress,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> UpgradeSnapshotsAsync(int version, Func<PlayRecord, bool> upgrade, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upgrade);

        // Active plays are skipped: the tracker holds them in memory and would write its own copy
        // back. They are picked up on a later run, once closed.
        return RewriteAsync(
            plays => plays.Where(play => play.SnapshotVersion < version && play.Status != PlayStatus.Active),
            upgrade,
            progress,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserSummary>> SummaryAsync(CancellationToken cancellationToken)
    {
        var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var rows = await context.Plays.AsNoTracking()
                .Where(play => !play.IsDeleted)
                .GroupBy(play => play.UserId)
                .Select(group => new
                {
                    UserId = group.Key,
                    Plays = group.Count(),
                    Scrobbles = group.Count(play => play.Scrobbled),
                    LastPlayedAt = group.Max(play => (DateTime?)play.StartedAt),
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return rows
                .Select(row => new UserSummary(row.UserId, row.Plays, row.Scrobbles, row.LastPlayedAt))
                .OrderByDescending(row => row.LastPlayedAt)
                .ToList();
        }
    }

    // Walks the selected records in id order, one locked batch at a time, saving every record
    // and announcing (a new sequence number) those the change reports as changed.
    private async Task<int> RewriteAsync(Func<IQueryable<PlayRecord>, IQueryable<PlayRecord>> select, Func<PlayRecord, bool> change, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        int total;
        var readContext = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (readContext.ConfigureAwait(false))
        {
            total = await select(readContext.Plays).CountAsync(cancellationToken).ConfigureAwait(false);
        }

        var changed = 0;
        var seen = 0;
        var lastId = string.Empty;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                await using (context.ConfigureAwait(false))
                {
                    var after = lastId;
#pragma warning disable CA1309 // Translated to SQLite's ordinal comparison.
                    var batch = await select(context.Plays)
                        .Where(play => string.Compare(play.Id, after) > 0)
#pragma warning restore CA1309
                        .OrderBy(play => play.Id)
                        .Take(RewriteBatch)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (batch.Count == 0)
                    {
                        break;
                    }

                    foreach (var play in batch)
                    {
                        if (change(play))
                        {
                            await StampAsync(context, play, cancellationToken).ConfigureAwait(false);
                            changed++;
                        }
                    }

                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    seen += batch.Count;
                    lastId = batch[^1].Id;
                }
            }
            finally
            {
                _writeLock.Release();
            }

            progress?.Report(total == 0 ? 100 : Math.Min(100, seen * 100.0 / total));
        }

        progress?.Report(100);
        return changed;
    }

    private static IQueryable<PlayRecord> Filter(IQueryable<PlayRecord> plays, HistoryQuery query)
    {
        plays = plays.Where(play => play.UserId == query.UserId && !play.IsDeleted);

        if (query.Types is { Count: > 0 } types)
        {
            plays = plays.Where(play => types.Contains(play.ItemType));
        }

        if (query.MediaTypes is { Count: > 0 } mediaTypes)
        {
            plays = plays.Where(play => play.MediaType != null && mediaTypes.Contains(play.MediaType));
        }

        if (query.ItemIds is { Count: > 0 } itemIds)
        {
            plays = plays.Where(play => itemIds.Contains(play.ItemId));
        }

        if (query.SeriesId is { } seriesId)
        {
            plays = plays.Where(play => play.SeriesId == seriesId);
        }

        if (query.AlbumId is { } albumId)
        {
            plays = plays.Where(play => play.AlbumId == albumId);
        }

        if (query.LibraryId is { } libraryId)
        {
            plays = plays.Where(play => play.LibraryId == libraryId);
        }

        if (!string.IsNullOrWhiteSpace(query.Artist))
        {
            var pattern = DelimitedList.ContainsPattern(query.Artist);
            plays = plays.Where(play => EF.Functions.Like(play.Artists, pattern, LikeEscape) || EF.Functions.Like(play.AlbumArtists, pattern, LikeEscape));
        }

        if (query.Genres is { Count: > 0 } genres)
        {
            var patterns = genres.Select(DelimitedList.ContainsPattern).ToList();
            plays = plays.Where(play => patterns.Any(pattern => EF.Functions.Like(play.Genres, pattern, LikeEscape)));
        }

        if (!string.IsNullOrWhiteSpace(query.ProviderId))
        {
            var pattern = DelimitedList.ContainsPattern(query.ProviderId);
            plays = plays.Where(play => EF.Functions.Like(play.ProviderIds, pattern, LikeEscape) || EF.Functions.Like(play.SeriesProviderIds, pattern, LikeEscape));
        }

        if (query.StartedAfter is { } startedAfter)
        {
            plays = plays.Where(play => play.StartedAt >= startedAfter);
        }

        if (query.StartedBefore is { } startedBefore)
        {
            plays = plays.Where(play => play.StartedAt < startedBefore);
        }

        if (query.Scrobbled is { } scrobbled)
        {
            plays = plays.Where(play => play.Scrobbled == scrobbled);
        }

        if (query.Statuses is { Count: > 0 } statuses)
        {
            plays = plays.Where(play => statuses.Contains(play.Status));
        }

        if (query.Sources is { Count: > 0 } sources)
        {
            plays = plays.Where(play => sources.Contains(play.Source));
        }

        if (!string.IsNullOrWhiteSpace(query.Client))
        {
            var client = query.Client;
            plays = plays.Where(play => play.Client == client);
        }

        if (!string.IsNullOrWhiteSpace(query.DeviceId))
        {
            var deviceId = query.DeviceId;
            plays = plays.Where(play => play.DeviceId == deviceId);
        }

        return plays;
    }

    // Callers hold the write lock, so sequence numbers are handed out and committed in order.
    private async Task StampAsync(RewindDbContext context, PlayRecord play, CancellationToken cancellationToken)
    {
        _lastSeq ??= await context.Plays.MaxAsync(row => (long?)row.Seq, cancellationToken).ConfigureAwait(false) ?? 0;
        _lastSeq++;
        play.Seq = _lastSeq.Value;
        play.UpdatedAt = _time.GetUtcNow().UtcDateTime;
    }
}
