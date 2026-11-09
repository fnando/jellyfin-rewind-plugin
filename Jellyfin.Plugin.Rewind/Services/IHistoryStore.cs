using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Rewind.Data;

namespace Jellyfin.Plugin.Rewind.Services;

/// <summary>
/// Reads and writes the play history. Every write is serialized and stamped with the next
/// change sequence, so a sequence number becomes visible only after every lower one.
/// </summary>
public interface IHistoryStore
{
    /// <summary>
    /// Inserts a record, stamping its sequence and update time.
    /// </summary>
    /// <param name="play">The record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the record is durable.</returns>
    Task InsertAsync(PlayRecord play, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a changed record.
    /// </summary>
    /// <param name="play">The record.</param>
    /// <param name="announce">Whether the change is meaningful to sync clients (status,
    /// scrobble, deletion): such changes take a new sequence number. Progress-only updates do not.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the change is durable.</returns>
    Task UpdateAsync(PlayRecord play, bool announce, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the most recent record for a session key.
    /// </summary>
    /// <param name="sessionKey">The session key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The record, if any.</returns>
    Task<PlayRecord?> FindBySessionKeyAsync(string sessionKey, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the most recent closed playback record of an item on a device that ended at or
    /// after a moment with less than a given time played, that neither counted as a scrobble nor
    /// was played to completion: a false start the client is retrying.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="itemId">The item id.</param>
    /// <param name="deviceId">The device id.</param>
    /// <param name="endedSince">The earliest end time (UTC).</param>
    /// <param name="maxPlayedTicks">The played time it must stay under.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The record, if any.</returns>
    Task<PlayRecord?> FindFalseStartAsync(Guid userId, Guid itemId, string? deviceId, DateTime endedSince, long maxPlayedTicks, CancellationToken cancellationToken);

    /// <summary>
    /// Lists a user's active records on one device.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="deviceId">The device id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The records.</returns>
    Task<IReadOnlyList<PlayRecord>> ActiveOnDeviceAsync(Guid userId, string deviceId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists active records last seen before a cutoff.
    /// </summary>
    /// <param name="cutoff">The cutoff (UTC).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The records.</returns>
    Task<IReadOnlyList<PlayRecord>> StaleAsync(DateTime cutoff, CancellationToken cancellationToken);

    /// <summary>
    /// Tells whether a user has a scrobbled play of an item that ended at or after a moment.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="itemId">The item id.</param>
    /// <param name="since">The moment (UTC).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> when there is one.</returns>
    Task<bool> HasRecentScrobbleAsync(Guid userId, Guid itemId, DateTime since, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a page of history.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The records, and whether more follow.</returns>
    Task<(IReadOnlyList<PlayRecord> Items, bool HasMore)> QueryAsync(HistoryQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the records of a user that changed after a sequence number, tombstones included.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="since">The last sequence number the client has seen.</param>
    /// <param name="limit">The page size.</param>
    /// <param name="types">Optional item kinds.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The records in sequence order, and whether more follow.</returns>
    Task<(IReadOnlyList<PlayRecord> Items, bool HasMore)> ChangesAsync(Guid userId, long since, int limit, IReadOnlyCollection<string>? types, CancellationToken cancellationToken);

    /// <summary>
    /// Gets one of a user's records.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="id">The record id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The record, unless missing or deleted.</returns>
    Task<PlayRecord?> GetAsync(Guid userId, string id, CancellationToken cancellationToken);

    /// <summary>
    /// Soft-deletes one of a user's records.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="id">The record id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> when a record was deleted.</returns>
    Task<bool> DeleteAsync(Guid userId, string id, CancellationToken cancellationToken);

    /// <summary>
    /// Erases a user's whole history (no tombstones: the user is gone).
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of records erased.</returns>
    Task<int> PurgeUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Re-applies the scrobble rule to every closed record, announcing the ones that flip.
    /// </summary>
    /// <param name="evaluate">The rule.</param>
    /// <param name="progress">Optional progress, 0..100.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of records that changed.</returns>
    Task<int> ReevaluateAsync(Func<PlayRecord, bool> evaluate, IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Brings closed records below a snapshot version up to date, announcing the ones that changed.
    /// </summary>
    /// <param name="version">The current snapshot version.</param>
    /// <param name="upgrade">Fills in a record (and raises its version); returns <c>true</c> when a field changed.</param>
    /// <param name="progress">Optional progress, 0..100.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of records that changed.</returns>
    Task<int> UpgradeSnapshotsAsync(int version, Func<PlayRecord, bool> upgrade, IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Summarizes the history per user.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One summary per user with history.</returns>
    Task<IReadOnlyList<UserSummary>> SummaryAsync(CancellationToken cancellationToken);
}
