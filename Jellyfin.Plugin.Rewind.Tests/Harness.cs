using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Rewind.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.Rewind.Tests;

/// <summary>
/// A tracker and store over an in-memory database and a manual clock.
/// </summary>
public sealed class Harness : IDisposable
{
    public static readonly DateTime T0 = new(2026, 11, 9, 20, 0, 0, DateTimeKind.Utc);

    public Harness()
    {
        Library.Setup(l => l.GetItemById(It.IsAny<Guid>())).Returns((BaseItem?)null);
        Library.Setup(l => l.GetCollectionFolders(It.IsAny<BaseItem>())).Returns(new List<Folder>());
        Store = new HistoryStore(Database, Time);
        Tracker = new PlaybackTracker(Store, new ItemSnapshotter(Library.Object, NullLogger<ItemSnapshotter>.Instance), Settings, Time, NullLogger<PlaybackTracker>.Instance);
    }

    public TestDatabase Database { get; } = new();

    public ManualTime Time { get; } = new(T0);

    public TestSettings Settings { get; } = new();

    public Mock<ILibraryManager> Library { get; } = new();

    public HistoryStore Store { get; }

    public PlaybackTracker Tracker { get; }

    public void Dispose()
    {
        Tracker.Dispose();
        Store.Dispose();
        Database.Dispose();
    }
}
