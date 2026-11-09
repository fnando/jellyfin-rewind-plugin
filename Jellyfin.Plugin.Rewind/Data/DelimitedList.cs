using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Jellyfin.Plugin.Rewind.Data;

/// <summary>
/// Encodes string lists as <c>|a|b|c|</c> so a single <c>LIKE '%|value|%'</c> finds an entry
/// without a join. A <c>|</c> inside a value becomes <c>/</c>.
/// </summary>
public static class DelimitedList
{
    private const char Separator = '|';

    /// <summary>
    /// Encodes values.
    /// </summary>
    /// <param name="values">The values.</param>
    /// <returns>The encoded list, or <c>null</c> when there are no non-blank values.</returns>
    public static string? Encode(IEnumerable<string?>? values)
    {
        var clean = values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Clean(value!))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (clean is null || clean.Length == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.Append(Separator);
        foreach (var value in clean)
        {
            builder.Append(value).Append(Separator);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Encodes a provider id dictionary as <c>key=value</c> entries.
    /// </summary>
    /// <param name="ids">The ids.</param>
    /// <returns>The encoded list, or <c>null</c> when empty.</returns>
    public static string? EncodePairs(IEnumerable<KeyValuePair<string, string>>? ids)
        => Encode(ids?
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => Pair(pair.Key, pair.Value)));

    /// <summary>
    /// Decodes a list made by <see cref="Encode"/>.
    /// </summary>
    /// <param name="encoded">The encoded list.</param>
    /// <returns>The values.</returns>
    public static IReadOnlyList<string> Decode(string? encoded)
        => string.IsNullOrEmpty(encoded)
            ? Array.Empty<string>()
            : encoded.Split(Separator, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Decodes a list made by <see cref="EncodePairs"/>.
    /// </summary>
    /// <param name="encoded">The encoded list.</param>
    /// <returns>The ids, or <c>null</c> when there are none.</returns>
    public static IReadOnlyDictionary<string, string>? DecodePairs(string? encoded)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in Decode(encoded))
        {
            var equals = entry.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                result[entry[..equals]] = entry[(equals + 1)..];
            }
        }

        return result.Count == 0 ? null : result;
    }

    /// <summary>
    /// Builds the <c>LIKE</c> pattern that matches one entry, escaping wildcards with <c>\</c>.
    /// </summary>
    /// <param name="value">The entry (a value, or <c>key=value</c>).</param>
    /// <returns>The pattern.</returns>
    public static string ContainsPattern(string value)
        => "%" + Separator + EscapeLike(Clean(value)) + Separator + "%";

    /// <summary>
    /// Builds a <c>key=value</c> entry.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns>The entry.</returns>
    public static string Pair(string key, string value)
        => Clean(key).Replace('=', '-') + "=" + Clean(value);

    private static string Clean(string value) => value.Trim().Replace(Separator, '/');

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
