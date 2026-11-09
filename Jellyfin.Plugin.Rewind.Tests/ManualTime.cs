using System;

namespace Jellyfin.Plugin.Rewind.Tests;

/// <summary>
/// A clock the test moves by hand.
/// </summary>
public sealed class ManualTime : TimeProvider
{
    public ManualTime(DateTime start)
    {
        Now = start;
    }

    public DateTime Now { get; set; }

    public void Advance(TimeSpan by) => Now += by;

    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Now, DateTimeKind.Utc));
}
