using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Rewind.Services;

/// <summary>
/// Subscribes to the server's playback and user data events and feeds them, in arrival order,
/// to the <see cref="PlaybackTracker"/>. Handlers only enqueue, so Jellyfin's request threads
/// never wait on the database and a failure never reaches the server.
/// </summary>
public sealed class PlaybackRecorder : BackgroundService
{
    private readonly ISessionManager _sessionManager;
    private readonly IUserDataManager _userDataManager;
    private readonly PlaybackTracker _tracker;
    private readonly ILogger<PlaybackRecorder> _logger;
    private readonly Channel<Func<CancellationToken, Task>> _queue = Channel.CreateUnbounded<Func<CancellationToken, Task>>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackRecorder"/> class.
    /// </summary>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="tracker">The tracker.</param>
    /// <param name="logger">The logger.</param>
    public PlaybackRecorder(ISessionManager sessionManager, IUserDataManager userDataManager, PlaybackTracker tracker, ILogger<PlaybackRecorder> logger)
    {
        _sessionManager = sessionManager;
        _userDataManager = userDataManager;
        _tracker = tracker;
        _logger = logger;
    }

    /// <summary>
    /// Splits a playback event into one signal per user watching (SyncPlay and shared sessions
    /// carry several).
    /// </summary>
    /// <param name="e">The event.</param>
    /// <returns>The signals; none when the event names no item or user.</returns>
    public static IReadOnlyList<PlaybackSignal> ToSignals(PlaybackProgressEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var itemId = e.Item?.Id ?? e.MediaInfo?.Id ?? Guid.Empty;
        if (itemId == Guid.Empty || e.Users is null)
        {
            return Array.Empty<PlaybackSignal>();
        }

        var completed = e is PlaybackStopEventArgs stop && stop.PlayedToCompletion;
        return e.Users
            .Where(user => user is not null)
            .Select(user => user.Id)
            .Distinct()
            .Select(userId => new PlaybackSignal
            {
                UserId = userId,
                ItemId = itemId,
                Item = e.Item,
                PlaySessionId = e.PlaySessionId,
                SessionId = e.Session?.Id,
                PositionTicks = e.PlaybackPositionTicks,
                IsPaused = e.IsPaused,
                PlayedToCompletion = completed,
                Client = e.ClientName ?? e.Session?.Client,
                DeviceName = e.DeviceName ?? e.Session?.DeviceName,
                DeviceId = e.DeviceId ?? e.Session?.DeviceId,
                PlayMethod = e.Session?.PlayState?.PlayMethod?.ToString(),
            })
            .ToList();
    }

    /// <inheritdoc />
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackProgress += OnPlaybackProgress;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _userDataManager.UserDataSaved += OnUserDataSaved;
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackProgress -= OnPlaybackProgress;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _userDataManager.UserDataSaved -= OnUserDataSaved;

        // Let the queued events drain, then write any progress still held in memory.
        _queue.Writer.TryComplete();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _tracker.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rewind: unable to write in-progress sessions on shutdown.");
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Drain to completion: StopAsync completes the writer, so the loop ends once the
        // events received before shutdown are handled.
        await foreach (var work in _queue.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            try
            {
                await work(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rewind: unable to record a playback event.");
            }
        }
    }

    private void Enqueue(PlaybackProgressEventArgs e, Func<PlaybackSignal, CancellationToken, Task> handle)
    {
        try
        {
            foreach (var signal in ToSignals(e))
            {
                _queue.Writer.TryWrite(token => handle(signal, token));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rewind: unable to read a playback event.");
        }
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e) => Enqueue(e, _tracker.StartAsync);

    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e) => Enqueue(e, _tracker.ProgressAsync);

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e) => Enqueue(e, _tracker.StopAsync);

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        if (e.SaveReason != UserDataSaveReason.TogglePlayed || e.UserData is not { Played: true } data || e.Item is not { } item)
        {
            return;
        }

        var userId = e.UserId;
        var playedAt = data.LastPlayedDate;
        _queue.Writer.TryWrite(token => _tracker.MarkPlayedAsync(userId, item, playedAt, token));
    }
}
