using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Rewind.Services;

/// <summary>
/// Closes sessions that stopped reporting (crashed clients, server restarts, clients that
/// never send a stop): once at startup, then every few minutes.
/// </summary>
public sealed class SessionReaper : BackgroundService
{
    /// <summary>
    /// How often the reaper runs.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly PlaybackTracker _tracker;
    private readonly TimeProvider _time;
    private readonly ILogger<SessionReaper> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionReaper"/> class.
    /// </summary>
    /// <param name="tracker">The tracker.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    public SessionReaper(PlaybackTracker tracker, TimeProvider time, ILogger<SessionReaper> logger)
    {
        _tracker = tracker;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, _time);
        do
        {
            try
            {
                var closed = await _tracker.ReapAsync(stoppingToken).ConfigureAwait(false);
                if (closed > 0)
                {
                    _logger.LogInformation("Rewind: closed {Count} abandoned playback session(s).", closed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Rewind: unable to close abandoned sessions.");
            }
        }
        while (await WaitAsync(timer, stoppingToken).ConfigureAwait(false));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
