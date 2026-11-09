using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Rewind.Data;
using Jellyfin.Plugin.Rewind.Services;
using Xunit;

namespace Jellyfin.Plugin.Rewind.Tests;

public class EncodingTests
{
    [Fact]
    public void DelimitedList_RoundTrips_AndCleansTheSeparator()
    {
        var encoded = DelimitedList.Encode(new[] { "AC|DC", " Daft Punk ", "", null, "Daft Punk" });

        Assert.Equal("|AC/DC|Daft Punk|", encoded);
        Assert.Equal(new[] { "AC/DC", "Daft Punk" }, DelimitedList.Decode(encoded));
        Assert.Null(DelimitedList.Encode(Array.Empty<string>()));
    }

    [Fact]
    public void ProviderIds_RoundTrip()
    {
        var encoded = DelimitedList.EncodePairs(new Dictionary<string, string> { ["Tmdb"] = "603", ["Imdb"] = "tt0133093", ["Empty"] = "" });

        Assert.Equal("|Imdb=tt0133093|Tmdb=603|", encoded);
        Assert.Equal(new Dictionary<string, string> { ["Imdb"] = "tt0133093", ["Tmdb"] = "603" }, DelimitedList.DecodePairs(encoded));
    }

    [Fact]
    public void ContainsPattern_EscapesWildcards()
    {
        Assert.Equal("%|50\\% off\\_now|%", DelimitedList.ContainsPattern("50% off_now"));
    }

    [Fact]
    public void Cursor_RoundTrips_AndRejectsGarbage()
    {
        var id = Guid.CreateVersion7().ToString("N");
        var cursor = new HistoryCursor(true, 638_000_000_000_000_000, id);

        Assert.True(HistoryCursor.TryParse(cursor.Encode(), out var parsed));
        Assert.Equal(cursor, parsed);

        Assert.False(HistoryCursor.TryParse("not-a-cursor", out _));
        Assert.False(HistoryCursor.TryParse("bm9wZQ", out _));
        Assert.False(HistoryCursor.TryParse(new string('a', 200), out _));
    }
}
