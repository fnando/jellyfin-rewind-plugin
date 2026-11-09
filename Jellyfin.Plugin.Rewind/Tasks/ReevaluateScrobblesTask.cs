using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Rewind.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Rewind.Tasks;

/// <summary>
/// Re-applies the current scrobble thresholds to the whole history. Run it after changing the
/// thresholds; plays that flip are announced on the changes feed.
/// </summary>
public class ReevaluateScrobblesTask : IScheduledTask
{
    private readonly IHistoryStore _store;
    private readonly ISettings _settings;
    private readonly ILogger<ReevaluateScrobblesTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReevaluateScrobblesTask"/> class.
    /// </summary>
    /// <param name="store">The history store.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="logger">The logger.</param>
    public ReevaluateScrobblesTask(IHistoryStore store, ISettings settings, ILogger<ReevaluateScrobblesTask> logger)
    {
        _store = store;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Re-evaluate scrobbles";

    /// <inheritdoc />
    public string Key => "RewindReevaluateScrobbles";

    /// <inheritdoc />
    public string Description => "Applies the current scrobble thresholds to every recorded play.";

    /// <inheritdoc />
    public string Category => "Rewind";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var settings = _settings.Current;
        var changed = await _store.ReevaluateAsync(play => ScrobblePolicy.Evaluate(play, settings), progress, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Rewind: re-evaluated scrobbles; {Count} play(s) changed.", changed);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();
}
