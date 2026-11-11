using System;
using System.Globalization;

namespace Jellyfin.Plugin.Rewind.Api;

/// <summary>
/// Parses the ISO 8601 times the API accepts. A time without an offset is taken as UTC.
/// </summary>
internal static class IsoTime
{
    /// <summary>
    /// Parses a time.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <param name="time">The time (UTC), on success.</param>
    /// <returns><c>true</c> when the text is a time.</returns>
    public static bool TryParse(string? value, out DateTime time)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            time = parsed.UtcDateTime;
            return true;
        }

        time = default;
        return false;
    }
}
