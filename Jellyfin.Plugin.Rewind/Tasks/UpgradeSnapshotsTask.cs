using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Rewind.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Rewind.Tasks;

/// <summary>
/// Fills in the item fields added in later plugin versions (disc number, genres) on plays
/// recorded before them, from the library. Runs at startup; records already up to date are
/// skipped, so it finishes at once when there is nothing to do. Plays whose item is gone from
/// the library keep the fields empty.
/// </summary>
public class UpgradeSnapshotsTask : IScheduledTask
{
    private readonly IHistoryStore _store;
    private readonly ItemSnapshotter _snapshotter;
    private readonly ILogger<UpgradeSnapshotsTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpgradeSnapshotsTask"/> class.
    /// </summary>
    /// <param name="store">The history store.</param>
    /// <param name="snapshotter">The item snapshotter.</param>
    /// <param name="logger">The logger.</param>
    public UpgradeSnapshotsTask(IHistoryStore store, ItemSnapshotter snapshotter, ILogger<UpgradeSnapshotsTask> logger)
    {
        _store = store;
        _snapshotter = snapshotter;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Fill in play details";

    /// <inheritdoc />
    public string Key => "RewindUpgradeSnapshots";

    /// <inheritdoc />
    public string Description => "Adds details introduced by plugin updates (disc number, genres) to plays recorded earlier.";

    /// <inheritdoc />
    public string Category => "Rewind";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var changed = await _store.UpgradeSnapshotsAsync(ItemSnapshotter.CurrentVersion, _snapshotter.Upgrade, progress, cancellationToken).ConfigureAwait(false);
        if (changed > 0)
        {
            _logger.LogInformation("Rewind: filled in details on {Count} play(s).", changed);
        }
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
    [
        new TaskTriggerInfo { Type = TaskTriggerInfoType.StartupTrigger },
    ];
}
