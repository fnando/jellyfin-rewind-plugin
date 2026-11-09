using System;
using System.Globalization;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Rewind.Data;
using Jellyfin.Plugin.Rewind.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jellyfin.Plugin.Rewind;

/// <summary>
/// Registers the plugin's services with the server.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddDbContextFactory<RewindDbContext>(options =>
            options.UseSqlite(string.Format(CultureInfo.InvariantCulture, "Data Source={0}", Plugin.Instance!.DatabasePath)));

        serviceCollection.TryAddSingleton(TimeProvider.System);
        serviceCollection.AddSingleton<ISettings, PluginSettings>();
        serviceCollection.AddSingleton<IHistoryStore, HistoryStore>();
        serviceCollection.AddSingleton<ItemSnapshotter>();
        serviceCollection.AddSingleton<PlaybackTracker>();

        // The database must exist before the recorder writes to it: hosted services start in
        // registration order.
        serviceCollection.AddHostedService<DatabaseInitializer>();
        serviceCollection.AddHostedService<PlaybackRecorder>();
        serviceCollection.AddHostedService<SessionReaper>();

        serviceCollection.AddScoped<IEventConsumer<UserDeletedEventArgs>, UserDeletedConsumer>();
    }
}
