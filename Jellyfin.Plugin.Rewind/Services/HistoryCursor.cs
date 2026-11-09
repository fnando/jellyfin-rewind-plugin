using System;
using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.Rewind.Services;

/// <summary>
/// A position in a history listing: the start time and id of the last record served, and the
/// direction it was read in. Travels to the client as an opaque base64url token
/// (<c>"{d|a}:{ticks}:{id}"</c>) so paging needs no server-side state and stays stable while
/// new plays are recorded.
/// </summary>
/// <param name="Descending">Whether the listing runs newest first.</param>
/// <param name="StartedTicks">The last record's start time, in UTC ticks.</param>
/// <param name="Id">The last record's id.</param>
public sealed record HistoryCursor(bool Descending, long StartedTicks, string Id)
{
    /// <summary>
    /// Encodes the cursor as an opaque token.
    /// </summary>
    /// <returns>The token.</returns>
    public string Encode()
    {
        var text = string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}", Descending ? 'd' : 'a', StartedTicks, Id);
        return Convert.ToBase64String(Encoding.ASCII.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Decodes a token produced by <see cref="Encode"/>.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <param name="cursor">The cursor, when the token is well-formed.</param>
    /// <returns><c>true</c> when the token was decoded.</returns>
    public static bool TryParse(string? token, out HistoryCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
        {
            return false;
        }

        try
        {
            var base64 = token.Trim().Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            var parts = Encoding.ASCII.GetString(Convert.FromBase64String(base64)).Split(':');
            if (parts.Length != 3 || parts[0] is not ("d" or "a"))
            {
                return false;
            }

            if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || !Guid.TryParseExact(parts[2], "N", out var id))
            {
                return false;
            }

            cursor = new HistoryCursor(parts[0] == "d", ticks, id.ToString("N"));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
