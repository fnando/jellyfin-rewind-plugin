using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Rewind.Data;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Rewind.Services;

/// <summary>
/// Turns the server's playback events into durable play records.
/// </summary>
/// <remarks>
/// <para>A record is written the moment playback starts, so a crash loses at most the progress
/// since the last write. Progress is accumulated in memory and written at most every
/// <c>ProgressWriteSeconds</c> (pause changes always write).</para>
/// <para>Missing events are tolerated: progress or stop without a start creates the record;
/// a session that goes quiet is closed as abandoned by <see cref="ReapAsync"/>; and progress
/// arriving for a recently abandoned session (the server restarted mid-playback) reopens it.</para>
/// </remarks>
public sealed class PlaybackTracker : IDisposable
{
    /// <summary>
    /// How long after being abandoned a session can still be reopened by its own events.
    /// </summary>
    public static readonly TimeSpan ReopenWindow = TimeSpan.FromHours(6);

    /// <summary>
    /// How close to a scrobbled play a "mark played" is taken as the same play.
    /// </summary>
    public static readonly TimeSpan ManualDedupWindow = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How far around a reported play a scrobbled play recorded from the server's events is
    /// taken as the same play.
    /// </summary>
    public static readonly TimeSpan ReportOverlapWindow = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How soon after a near-empty session ends a new start of the same item on the same device
    /// is taken as the client retrying, not a new play.
    /// </summary>
    public static readonly TimeSpan FalseStartWindow = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The most a session may have played and still count as a false start.
    /// </summary>
    public static readonly TimeSpan FalseStartMaxPlayed = TimeSpan.FromSeconds(10);

    // Played time may run ahead of the wall clock by at most this factor (fast playback);
    // anything beyond is a seek and does not count as played.
    private const double MaxPlaybackRate = 2.0;

    private readonly IHistoryStore _store;
    private readonly ItemSnapshotter _snapshotter;
    private readonly ISettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<PlaybackTracker> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, LiveSession> _live = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackTracker"/> class.
    /// </summary>
    /// <param name="store">The history store.</param>
    /// <param name="snapshotter">The item snapshotter.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    public PlaybackTracker(IHistoryStore store, ItemSnapshotter snapshotter, ISettings settings, TimeProvider time, ILogger<PlaybackTracker> logger)
    {
        _store = store;
        _snapshotter = snapshotter;
        _settings = settings;
        _time = time;
        _logger = logger;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    /// <inheritdoc />
    public void Dispose() => _lock.Dispose();

    /// <summary>
    /// Handles a playback start.
    /// </summary>
    /// <param name="signal">The event.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the record is durable.</returns>
    public async Task StartAsync(PlaybackSignal signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = Now;
            var key = signal.SessionKey;

            // A device plays one thing at a time: whatever it was playing before has ended,
            // whether or not its stop event arrived.
            if (!string.IsNullOrEmpty(signal.DeviceId))
            {
                foreach (var other in await _store.ActiveOnDeviceAsync(signal.UserId, signal.DeviceId, cancellationToken).ConfigureAwait(false))
                {
                    if (!string.Equals(other.SessionKey, key, StringComparison.Ordinal))
                    {
                        // No stop came: it most likely played on until this start (auto-advance).
                        // That holds for a play loaded back from disk after a restart, too.
                        var live = _live.Remove(other.SessionKey, out var held) ? held : Restore(other);
                        _ = EstimateTo(live, now);
                        await CloseAsync(live.Play, PlayStatus.Abandoned, live.Play.LastSeenAt, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            var session = await ResumeAsync(key, reopen: false, now, cancellationToken).ConfigureAwait(false);
            if (session is null)
            {
                await CreateAsync(signal, now, cancellationToken).ConfigureAwait(false);
                return;
            }

            // A repeated start for a session that is already playing.
            await AdvanceAsync(session, signal, now, force: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Handles a progress report.
    /// </summary>
    /// <param name="signal">The event.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the report is handled.</returns>
    public async Task ProgressAsync(PlaybackSignal signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = Now;
            var session = await ResumeAsync(signal.SessionKey, reopen: true, now, cancellationToken).ConfigureAwait(false);
            if (session is null)
            {
                // The start was missed (plugin installed mid-playback, start event lost).
                await CreateAsync(signal, now, cancellationToken).ConfigureAwait(false);
                return;
            }

            await AdvanceAsync(session, signal, now, force: false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Handles a playback stop: the record is closed and its scrobble decided.
    /// </summary>
    /// <param name="signal">The event.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the record is durable.</returns>
    public async Task StopAsync(PlaybackSignal signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = Now;
            var key = signal.SessionKey;
            var session = await ResumeAsync(key, reopen: true, now, cancellationToken).ConfigureAwait(false)
                ?? await CreateAsync(signal, now, cancellationToken).ConfigureAwait(false);

            if (signal.PositionTicks is not null)
            {
                Apply(session, signal, now);
            }
            else
            {
                // The client did not say where it stopped: assume it played on until now (or
                // until the end of the item). A paused session just ends where it was.
                if (!EstimateTo(session, now))
                {
                    Advance(session, session.Play.PositionTicks, signal.IsPaused, now);
                }

                Describe(session.Play, signal);
            }

            session.Play.PlayedToCompletion |= signal.PlayedToCompletion;
            _live.Remove(key);
            await CloseAsync(session.Play, PlayStatus.Completed, session.Play.LastSeenAt, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Records an item the user marked as played.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="item">The item.</param>
    /// <param name="playedAt">The date the user gave, if any.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> when a record was written.</returns>
    public async Task<bool> MarkPlayedAsync(Guid userId, BaseItem item, DateTime? playedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        // Marking a series or album played marks each of its items; the folder itself is not a play.
        if (item.IsFolder)
        {
            return false;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = Now;
            if (await _store.HasRecentScrobbleAsync(userId, item.Id, now - ManualDedupWindow, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            var at = playedAt is { } date ? DateTime.SpecifyKind(date, date.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : date.Kind).ToUniversalTime() : now;
            var play = new PlayRecord
            {
                Id = NewId(),
                UserId = userId,
                Source = PlaySource.Manual,
                Status = PlayStatus.Completed,
                StartedAt = at,
                LastSeenAt = at,
                EndedAt = at,
                Scrobbled = true,
            };
            _snapshotter.Apply(play, item.Id, item);
            play.SessionKey = FormattableString.Invariant($"{userId:N}:{item.Id:N}:manual:{play.Id}");
            play.PositionTicks = play.RuntimeTicks ?? 0;
            play.MaxPositionTicks = play.PositionTicks;
            await _store.InsertAsync(play, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Records a play a client reports after the fact. Sending the same report again (same
    /// user, item and start second) returns the existing record, and so does a report that
    /// overlaps a scrobbled play the server recorded from its own playback events.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="item">The item.</param>
    /// <param name="report">The reported play.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The new or existing record.</returns>
    public async Task<ReportResult> ReportAsync(Guid userId, BaseItem item, ReportedPlay report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(report);

        // Whole seconds: a resend must match even if the client serializes the time differently.
        var startedAt = Utc(report.StartedAt);
        startedAt = new DateTime(startedAt.Ticks - (startedAt.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        var key = FormattableString.Invariant($"{userId:N}:{item.Id:N}:scrobble:{startedAt.Ticks}");

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await _store.FindBySessionKeyAsync(key, cancellationToken).ConfigureAwait(false) is { } existing)
            {
                return new ReportResult(ReportOutcome.Duplicate, existing);
            }

            var play = new PlayRecord
            {
                Id = NewId(),
                UserId = userId,
                SessionKey = key,
                Source = PlaySource.Scrobble,
                Status = PlayStatus.Completed,
                StartedAt = startedAt,
                Client = report.Client,
                DeviceName = report.DeviceName,
                DeviceId = report.DeviceId,
            };
            _snapshotter.Apply(play, item.Id, item);

            // Fill in what the client left out: without a played time, assume it played the item
            // through (or, failing a runtime, for the whole time it reported).
            var runtime = play.RuntimeTicks;
            var span = report.EndedAt is { } ended ? Math.Max(0, (Utc(ended) - startedAt).Ticks) : (long?)null;
            var played = report.PlayedTicks
                ?? (runtime, span) switch
                {
                    ({ } r, { } s) => Math.Min(r, s),
                    ({ } r, null) => r,
                    (null, { } s) => s,
                    _ => 0,
                };
            var position = report.PositionTicks ?? (runtime is { } length ? Math.Min(played, length) : played);
            var endedAt = report.EndedAt is { } end ? Utc(end) : startedAt + TimeSpan.FromTicks(played);

            play.PlayedTicks = played;
            play.PositionTicks = position;
            play.MaxPositionTicks = position;
            play.PlayedToCompletion = runtime is { } total && position >= total;
            play.LastSeenAt = endedAt;
            play.EndedAt = endedAt;

            var overlap = await _store.FindOverlappingScrobbleAsync(userId, item.Id, startedAt - ReportOverlapWindow, endedAt + ReportOverlapWindow, cancellationToken).ConfigureAwait(false);
            if (overlap is not null)
            {
                return new ReportResult(ReportOutcome.Duplicate, overlap);
            }

            play.Scrobbled = ScrobblePolicy.Evaluate(play, _settings.Current);
            await _store.InsertAsync(play, cancellationToken).ConfigureAwait(false);
            return new ReportResult(ReportOutcome.Created, play);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Closes the sessions that went quiet for longer than <c>StaleSessionMinutes</c>.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of sessions closed.</returns>
    public async Task<int> ReapAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cutoff = Now - TimeSpan.FromMinutes(Math.Max(1, _settings.Current.StaleSessionMinutes));
            var closed = 0;

            foreach (var stale in await _store.StaleAsync(cutoff, cancellationToken).ConfigureAwait(false))
            {
                // The in-memory copy may have newer, not yet written, progress.
                var play = _live.TryGetValue(stale.SessionKey, out var live) ? live.Play : stale;
                if (play.LastSeenAt >= cutoff)
                {
                    continue;
                }

                _live.Remove(stale.SessionKey);
                await CloseAsync(play, PlayStatus.Abandoned, play.LastSeenAt, cancellationToken).ConfigureAwait(false);
                closed++;
            }

            return closed;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Writes the progress held in memory (on shutdown).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when everything is written.</returns>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var session in _live.Values.Where(session => session.Dirty))
            {
                await PersistAsync(session, announce: false, Now, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string NewId() => Guid.CreateVersion7().ToString("N");

    private static DateTime Utc(DateTime value)
        => DateTime.SpecifyKind(value, value.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : value.Kind).ToUniversalTime();

    private static void Apply(LiveSession session, PlaybackSignal signal, DateTime now)
    {
        Advance(session, signal.PositionTicks ?? session.Play.PositionTicks, signal.IsPaused, now);
        Describe(session.Play, signal);
    }

    // Some clients only learn the device or method after the first report.
    private static void Describe(PlayRecord play, PlaybackSignal signal)
    {
        play.Client ??= signal.Client;
        play.DeviceName ??= signal.DeviceName;
        play.DeviceId ??= signal.DeviceId;
        play.PlayMethod = signal.PlayMethod ?? play.PlayMethod;
    }

    // Moves a session to a position reached at a moment: time played grows by the position
    // moved, but never faster than the wall clock allows (anything faster is a seek).
    private static void Advance(LiveSession session, long position, bool paused, DateTime at)
    {
        var play = session.Play;
        if (!session.Paused)
        {
            var wall = (at - session.LastEventAt).Ticks;
            var moved = position - play.PositionTicks;
            if (wall > 0 && moved > 0)
            {
                play.PlayedTicks += Math.Min(moved, (long)(wall * MaxPlaybackRate));
            }
        }

        play.PositionTicks = position;
        play.MaxPositionTicks = Math.Max(play.MaxPositionTicks, position);
        play.LastSeenAt = Max(play.LastSeenAt, at);
        session.LastEventAt = Max(session.LastEventAt, at);
        session.Paused = paused;
        play.IsPaused = paused;
        session.Dirty = true;
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    // A play loaded from disk (after a restart, or held by an earlier tracker) resumes from its
    // last written state: the last event is when it was last seen.
    private static LiveSession Restore(PlayRecord play)
        => new(play, play.LastSeenAt, play.LastSeenAt) { Paused = play.IsPaused };

    // Assumes a playing (not paused) session kept playing from its last report until now, without
    // going past the end of the item; the play then ends where the item did. Used when the client
    // does not say where it stopped. Not applied after a gap longer than the stale timeout: a
    // session that quiet is abandoned, not playing.
    private bool EstimateTo(LiveSession session, DateTime now)
    {
        var elapsed = now - session.LastEventAt;
        var staleAfter = TimeSpan.FromMinutes(Math.Max(1, _settings.Current.StaleSessionMinutes));
        if (session.Paused || elapsed <= TimeSpan.Zero || elapsed > staleAfter)
        {
            return false;
        }

        var play = session.Play;
        var position = play.PositionTicks + elapsed.Ticks;
        var at = now;
        if (play.RuntimeTicks is > 0 and var runtime && position > runtime)
        {
            at = session.LastEventAt + TimeSpan.FromTicks(Math.Max(0, runtime - play.PositionTicks));
            position = Math.Max(runtime, play.PositionTicks);
        }

        Advance(session, position, paused: false, at);
        return true;
    }

    private async Task<LiveSession?> ResumeAsync(string key, bool reopen, DateTime now, CancellationToken cancellationToken)
    {
        if (_live.TryGetValue(key, out var live))
        {
            return live;
        }

        var play = await _store.FindBySessionKeyAsync(key, cancellationToken).ConfigureAwait(false);
        if (play is null)
        {
            return null;
        }

        if (play.Status == PlayStatus.Active)
        {
            return _live[key] = Restore(play);
        }

        if (reopen && play.Status == PlayStatus.Abandoned && play.LastSeenAt >= now - ReopenWindow)
        {
            _logger.LogDebug("Rewind: reopening abandoned session {Key}.", key);
            play.Status = PlayStatus.Active;
            play.EndedAt = null;
            var session = _live[key] = Restore(play);
            await _store.UpdateAsync(play, announce: true, cancellationToken).ConfigureAwait(false);
            return session;
        }

        return null;
    }

    private async Task<LiveSession> CreateAsync(PlaybackSignal signal, DateTime now, CancellationToken cancellationToken)
    {
        var position = signal.PositionTicks ?? 0;

        // Some clients start, stop at once and start again with a new play session id (a
        // retry, a buffering restart). That is one play: carry on with the false start's record.
        var falseStart = await _store.FindFalseStartAsync(signal.UserId, signal.ItemId, signal.DeviceId, now - FalseStartWindow, FalseStartMaxPlayed.Ticks, cancellationToken).ConfigureAwait(false);
        if (falseStart is not null)
        {
            _logger.LogDebug("Rewind: continuing false start {Id} as session {Key}.", falseStart.Id, signal.SessionKey);
            falseStart.SessionKey = signal.SessionKey;
            falseStart.PlaySessionId = signal.PlaySessionId;
            falseStart.Status = PlayStatus.Active;
            falseStart.EndedAt = null;
            falseStart.Scrobbled = false;
            falseStart.PlayedToCompletion = false;
            falseStart.PositionTicks = position;
            falseStart.MaxPositionTicks = Math.Max(falseStart.MaxPositionTicks, position);
            falseStart.LastSeenAt = now;
            falseStart.IsPaused = signal.IsPaused;
            await _store.UpdateAsync(falseStart, announce: true, cancellationToken).ConfigureAwait(false);

            var resumed = new LiveSession(falseStart, now, now) { Paused = signal.IsPaused };
            _live[signal.SessionKey] = resumed;
            return resumed;
        }

        var play = new PlayRecord
        {
            Id = NewId(),
            UserId = signal.UserId,
            SessionKey = signal.SessionKey,
            PlaySessionId = signal.PlaySessionId,
            Source = PlaySource.Playback,
            Status = PlayStatus.Active,
            StartedAt = now,
            LastSeenAt = now,
            PositionTicks = position,
            MaxPositionTicks = position,
            IsPaused = signal.IsPaused,
            Client = signal.Client,
            DeviceName = signal.DeviceName,
            DeviceId = signal.DeviceId,
            PlayMethod = signal.PlayMethod,
        };
        _snapshotter.Apply(play, signal.ItemId, signal.Item);

        await _store.InsertAsync(play, cancellationToken).ConfigureAwait(false);
        var session = new LiveSession(play, now, now) { Paused = signal.IsPaused };
        _live[signal.SessionKey] = session;
        return session;
    }

    private async Task AdvanceAsync(LiveSession session, PlaybackSignal signal, DateTime now, bool force, CancellationToken cancellationToken)
    {
        var pauseChanged = session.Paused != signal.IsPaused;
        Apply(session, signal, now);

        var interval = TimeSpan.FromSeconds(Math.Max(0, _settings.Current.ProgressWriteSeconds));
        if (force || pauseChanged || now - session.LastWrittenAt >= interval)
        {
            await PersistAsync(session, announce: false, now, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PersistAsync(LiveSession session, bool announce, DateTime now, CancellationToken cancellationToken)
    {
        await _store.UpdateAsync(session.Play, announce, cancellationToken).ConfigureAwait(false);
        session.LastWrittenAt = now;
        session.Dirty = false;
    }

    private async Task CloseAsync(PlayRecord play, PlayStatus status, DateTime endedAt, CancellationToken cancellationToken)
    {
        // A play recorded by an older plugin version and still running when it was updated
        // missed the startup backfill (active plays are skipped): bring it up to date now.
        _snapshotter.Upgrade(play);

        play.Status = status;
        play.EndedAt = endedAt;
        play.Scrobbled = ScrobblePolicy.Evaluate(play, _settings.Current);
        await _store.UpdateAsync(play, announce: true, cancellationToken).ConfigureAwait(false);
    }

    private sealed class LiveSession
    {
        public LiveSession(PlayRecord play, DateTime lastEventAt, DateTime lastWrittenAt)
        {
            Play = play;
            LastEventAt = lastEventAt;
            LastWrittenAt = lastWrittenAt;
        }

        public PlayRecord Play { get; }

        public DateTime LastEventAt { get; set; }

        public DateTime LastWrittenAt { get; set; }

        public bool Paused { get; set; }

        public bool Dirty { get; set; }
    }
}
