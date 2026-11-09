using System;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using MediaBrowser.Controller.Events;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Rewind.Services;

/// <summary>
/// Erases a deleted user's history, when <c>PurgeHistoryOnUserDelete</c> is on.
/// </summary>
public sealed class UserDeletedConsumer : IEventConsumer<UserDeletedEventArgs>
{
    private readonly IHistoryStore _store;
    private readonly ISettings _settings;
    private readonly ILogger<UserDeletedConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserDeletedConsumer"/> class.
    /// </summary>
    /// <param name="store">The history store.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="logger">The logger.</param>
    public UserDeletedConsumer(IHistoryStore store, ISettings settings, ILogger<UserDeletedConsumer> logger)
    {
        _store = store;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task OnEvent(UserDeletedEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);

        if (!_settings.Current.PurgeHistoryOnUserDelete || eventArgs.Argument is not { } user)
        {
            return;
        }

        try
        {
            var erased = await _store.PurgeUserAsync(user.Id, default).ConfigureAwait(false);
            _logger.LogInformation("Rewind: erased {Count} play(s) of deleted user {UserId}.", erased, user.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rewind: unable to erase the history of deleted user {UserId}.", user.Id);
        }
    }
}
