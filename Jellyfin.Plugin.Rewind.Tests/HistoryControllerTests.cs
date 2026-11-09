using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Rewind.Api;
using Jellyfin.Plugin.Rewind.Data;
using Jellyfin.Plugin.Rewind.Models;
using Jellyfin.Plugin.Rewind.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Rewind.Tests;

public sealed class HistoryControllerTests : IDisposable
{
    private static readonly long Minute = TimeSpan.FromMinutes(1).Ticks;

    private readonly Harness _h = new();
    private readonly Mock<IUserManager> _users = new();
    private readonly Guid _alice = Guid.NewGuid();
    private readonly Guid _bob = Guid.NewGuid();
    private readonly Guid _album = Guid.NewGuid();
    private readonly Guid _series = Guid.NewGuid();

    public HistoryControllerTests()
    {
        _users.Setup(u => u.GetUserById(It.IsAny<Guid>())).Returns((User?)null);
        _users.Setup(u => u.GetUserById(_alice)).Returns(new User("alice", "default", "default"));
        _users.Setup(u => u.GetUserById(_bob)).Returns(new User("bob", "default", "default"));
    }

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task History_DefaultsToScrobbles_NewestFirst_ForTheCaller()
    {
        await Seed();

        var page = await Get(As(_alice));

        Assert.Equal(new[] { "Track B", "Track A", "Episode 1", "The Matrix" }, page.Items.Select(item => item.Name));
        Assert.All(page.Items, item => Assert.Equal(_alice, item.UserId));
        Assert.All(page.Items, item => Assert.True(item.Scrobbled));
        Assert.Null(page.NextCursor);
    }

    [Theory]
    [InlineData("types=Movie", "The Matrix")]
    [InlineData("types=movie,EPISODE", "Episode 1,The Matrix")]
    [InlineData("mediaTypes=Audio", "Track B,Track A")]
    [InlineData("artist=daft punk", "Track B,Track A")]
    [InlineData("genres=sci-fi", "The Matrix")]
    [InlineData("genres=House,Action", "Track A,The Matrix")]
    [InlineData("providerId=imdb:tt0133093", "The Matrix")]
    [InlineData("providerId=Tvdb=81189", "Episode 1")]
    [InlineData("scrobbled=false", "Skipped")]
    [InlineData("scrobbled=any", "Track B,Track A,Skipped,Episode 1,The Matrix")]
    [InlineData("source=manual", "Track B")]
    [InlineData("status=completed&source=playback&scrobbled=any", "Track A,Skipped,Episode 1,The Matrix")]
    [InlineData("client=Finamp", "Track A")]
    [InlineData("order=asc", "The Matrix,Episode 1,Track A,Track B")]
    public async Task History_Filters(string query, string expected)
    {
        await Seed();

        var page = await Get(As(_alice), Parse(query));

        Assert.Equal(expected.Split(','), page.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task History_FiltersByIds_AndTime()
    {
        await Seed();

        Assert.Equal(new[] { "Track A" }, (await Get(As(_alice), new() { ["albumId"] = _album.ToString() })).Items.Where(i => i.Source == "playback").Select(i => i.Name));
        Assert.Equal(new[] { "Episode 1" }, (await Get(As(_alice), new() { ["seriesId"] = _series.ToString() })).Items.Select(i => i.Name));
        Assert.Equal(
            new[] { "Track B", "Track A" },
            (await Get(As(_alice), new() { ["startedAfter"] = Harness.T0.AddHours(3).ToString("O") })).Items.Select(i => i.Name));
        Assert.Equal(
            new[] { "The Matrix" },
            (await Get(As(_alice), new() { ["startedBefore"] = "2026-11-09T21:00:00Z" })).Items.Select(i => i.Name));
    }

    [Fact]
    public async Task Paging_WalksEveryPlayOnce_WhilePlaysKeepArriving()
    {
        for (var i = 0; i < 7; i++)
        {
            await Play(_alice, "Movie " + i, 10, 10);
        }

        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var query = new Dictionary<string, string> { ["limit"] = "3" };
            if (cursor is not null)
            {
                query["cursor"] = cursor;
            }

            var page = await Get(As(_alice), query);
            seen.AddRange(page.Items.Select(item => item.Name!));
            cursor = page.NextCursor;
            pages++;

            // A new play arriving mid-walk must not shift the pages.
            await Play(_alice, "Late " + pages, 10, 10);
        }
        while (cursor is not null);

        Assert.Equal(3, pages);
        Assert.Equal(Enumerable.Range(0, 7).Reverse().Select(i => "Movie " + i), seen);
    }

    [Theory]
    [InlineData("limit", "0")]
    [InlineData("limit", "1001")]
    [InlineData("types", "Spaceship")]
    [InlineData("mediaTypes", "Smell")]
    [InlineData("scrobbled", "maybe")]
    [InlineData("order", "sideways")]
    [InlineData("cursor", "garbage")]
    [InlineData("startedAfter", "yesterday")]
    [InlineData("providerId", "tt0133093")]
    [InlineData("seriesId", "nope")]
    [InlineData("status", "paused")]
    public async Task BadParameters_Answer400(string name, string value)
    {
        var result = await Call(As(_alice), new() { [name] = value });

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.IsType<ErrorResponse>(bad.Value);
    }

    [Fact]
    public async Task ACursor_IsBoundToItsOrder()
    {
        for (var i = 0; i < 3; i++)
        {
            await Play(_alice, "Movie " + i, 10, 10);
        }

        var page = await Get(As(_alice), new() { ["limit"] = "1" });
        var result = await Call(As(_alice), new() { ["cursor"] = page.NextCursor!, ["order"] = "asc" });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Users_OnlySeeTheirOwnHistory()
    {
        await Seed();
        await Play(_bob, "Bob's movie", 10, 10);

        Assert.Equal(new[] { "Bob's movie" }, (await Get(As(_bob))).Items.Select(i => i.Name));

        var forbidden = await Call(As(_alice), new() { ["userId"] = _bob.ToString() });
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(forbidden.Result).StatusCode);

        // Naming yourself is fine.
        Assert.Equal(4, (await Get(As(_alice), new() { ["userId"] = _alice.ToString() })).Items.Count);
    }

    [Fact]
    public async Task Administrators_AndApiKeys_CanReadAnyUser()
    {
        await Play(_bob, "Bob's movie", 10, 10);

        Assert.Single((await Get(As(_alice, admin: true), new() { ["userId"] = _bob.ToString() })).Items);
        Assert.Single((await Get(ApiKey(), new() { ["userId"] = _bob.ToString() })).Items);

        var unknown = await Call(ApiKey(), new() { ["userId"] = Guid.NewGuid().ToString() });
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsType<ObjectResult>(unknown.Result).StatusCode);

        var noUser = await Call(ApiKey(), new());
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(noUser.Result).StatusCode);
    }

    [Fact]
    public async Task Changes_ReportCreations_Updates_AndDeletions_InOrder()
    {
        await Play(_alice, "First", 10, 10);
        await Play(_alice, "Second", 10, 1);
        await Play(_bob, "Not mine", 10, 10);

        var controller = Controller(As(_alice));
        var all = Assert.IsType<ChangesPage>((await controller.GetChanges(null, 0, null, null, CancellationToken.None)).Value);
        Assert.Equal(new[] { "First", "Second" }, all.Items.Select(i => i.Name));
        Assert.False(all.HasMore);

        // Nothing new: the same since comes back.
        var none = Assert.IsType<ChangesPage>((await controller.GetChanges(null, all.NextSince, null, null, CancellationToken.None)).Value);
        Assert.Empty(none.Items);
        Assert.Equal(all.NextSince, none.NextSince);

        var first = all.Items[0];
        Assert.IsType<NoContentResult>(await controller.DeleteEntry(first.Id, null, CancellationToken.None));

        var after = Assert.IsType<ChangesPage>((await controller.GetChanges(null, all.NextSince, null, null, CancellationToken.None)).Value);
        var tombstone = Assert.Single(after.Items);
        Assert.Equal(first.Id, tombstone.Id);
        Assert.True(tombstone.Deleted);

        // Gone from the history and from single reads.
        Assert.DoesNotContain((await Get(As(_alice), new() { ["scrobbled"] = "any" })).Items, i => i.Id == first.Id);
        Assert.IsType<NotFoundObjectResult>((await controller.GetEntry(first.Id, null, CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Changes_Page_WithHasMore()
    {
        for (var i = 0; i < 5; i++)
        {
            await Play(_alice, "Movie " + i, 10, 10);
        }

        var controller = Controller(As(_alice));
        var page = Assert.IsType<ChangesPage>((await controller.GetChanges(null, 0, null, 2, CancellationToken.None)).Value);
        Assert.Equal(2, page.Items.Count);
        Assert.True(page.HasMore);
        Assert.Equal(page.Items[^1].Seq, page.NextSince);
    }

    [Fact]
    public async Task Users_CannotDeleteOtherUsersPlays()
    {
        await Play(_bob, "Bob's movie", 10, 10);
        var id = Assert.Single((await Get(As(_bob))).Items).Id;

        Assert.IsType<NotFoundObjectResult>(await Controller(As(_alice)).DeleteEntry(id, null, CancellationToken.None));
        Assert.Single((await Get(As(_bob))).Items);
    }

    [Fact]
    public async Task Reevaluate_FlipsPlays_AndAnnouncesThem()
    {
        await Play(_alice, "Half", 10, 5);
        var before = Assert.Single((await Get(As(_alice), new() { ["scrobbled"] = "any" })).Items);
        Assert.False(before.Scrobbled);

        _h.Settings.Current.Video.Percent = 50;
        var changed = await _h.Store.ReevaluateAsync(play => ScrobblePolicy.Evaluate(play, _h.Settings.Current), null, CancellationToken.None);

        Assert.Equal(1, changed);
        var after = Assert.Single((await Get(As(_alice))).Items);
        Assert.True(after.Scrobbled);
        Assert.True(after.Seq > before.Seq);
    }

    private static Dictionary<string, string> Parse(string query)
        => query.Split('&').Select(part => part.Split('=', 2)).ToDictionary(pair => pair[0], pair => pair[1]);

    private static ClaimsPrincipal ApiKey()
        => new(new ClaimsIdentity(
            new[]
            {
                new Claim(RequestUser.UserIdClaim, Guid.Empty.ToString()),
                new Claim(ClaimTypes.Role, RequestUser.AdministratorRole),
                new Claim("Jellyfin-IsApiKey", "true"),
            },
            "Custom"));

    private static ClaimsPrincipal As(Guid userId, bool admin = false)
        => new(new ClaimsIdentity(
            new[]
            {
                new Claim(RequestUser.UserIdClaim, userId.ToString()),
                new Claim(ClaimTypes.Role, admin ? RequestUser.AdministratorRole : "User"),
            },
            "Custom"));

    private HistoryController Controller(ClaimsPrincipal user)
        => new(_h.Store, _users.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } },
        };

    private async Task<HistoryPage> Get(ClaimsPrincipal user, Dictionary<string, string>? query = null)
    {
        var result = await Call(user, query ?? new());
        return Assert.IsType<HistoryPage>(result.Value);
    }

    private Task<ActionResult<HistoryPage>> Call(ClaimsPrincipal user, Dictionary<string, string> q)
    {
        string? One(string name) => q.TryGetValue(name, out var value) ? value : null;
        string[]? Many(string name) => q.TryGetValue(name, out var value) ? new[] { value } : null;

        return Controller(user).GetHistory(
            One("userId"),
            Many("types"),
            Many("mediaTypes"),
            Many("itemIds"),
            One("seriesId"),
            One("albumId"),
            One("libraryId"),
            One("artist"),
            Many("genres"),
            One("providerId"),
            One("startedAfter"),
            One("startedBefore"),
            One("scrobbled"),
            Many("status"),
            Many("source"),
            One("client"),
            One("deviceId"),
            One("order"),
            q.TryGetValue("limit", out var limit) ? int.Parse(limit, System.Globalization.CultureInfo.InvariantCulture) : null,
            One("cursor"),
            CancellationToken.None);
    }

    // Seeds Alice's history, one hour apart:
    //   The Matrix (movie, scrobbled), Episode 1 (scrobbled), Skipped (movie, not scrobbled),
    //   Track A (audio via Finamp, scrobbled), Track B (manual mark-played).
    private async Task Seed()
    {
        await Play(_alice, "The Matrix", 100, 90, play =>
        {
            play.ItemType = "Movie";
            play.ProviderIds = "|Imdb=tt0133093|Tmdb=603|";
            play.Genres = "|Action|Sci-Fi|";
        });
        await Play(_alice, "Episode 1", 40, 39, play =>
        {
            play.ItemType = "Episode";
            play.SeriesId = _series;
            play.SeriesName = "Breaking Bad";
            play.ProviderIds = "|Tvdb=349232|";
            play.SeriesProviderIds = "|Tvdb=81189|";
        });
        await Play(_alice, "Skipped", 100, 3);
        await Play(_alice, "Track A", 4, 4, play =>
        {
            play.ItemType = "Audio";
            play.MediaType = "Audio";
            play.AlbumId = _album;
            play.Artists = "|Daft Punk|";
            play.Genres = "|House|";
            play.Client = "Finamp";
        });
        await Play(_alice, "Track B", 4, 0, play =>
        {
            play.ItemType = "Audio";
            play.MediaType = "Audio";
            play.AlbumId = _album;
            play.AlbumArtists = "|Daft Punk|";
            play.Source = PlaySource.Manual;
        });
    }

    // Writes a closed play straight to the store, an hour after the previous one.
    private async Task Play(Guid userId, string name, int runtimeMinutes, int watchedMinutes, Action<PlayRecord>? configure = null)
    {
        var at = _h.Time.Now;
        _h.Time.Advance(TimeSpan.FromHours(1));
        var play = new PlayRecord
        {
            Id = Guid.CreateVersion7().ToString("N"),
            UserId = userId,
            ItemId = Guid.NewGuid(),
            SessionKey = Guid.NewGuid().ToString("N"),
            ItemType = "Movie",
            MediaType = "Video",
            Name = name,
            StartedAt = at,
            LastSeenAt = at.AddMinutes(watchedMinutes),
            EndedAt = at.AddMinutes(watchedMinutes),
            Status = PlayStatus.Completed,
            RuntimeTicks = runtimeMinutes * Minute,
            PlayedTicks = watchedMinutes * Minute,
            PositionTicks = watchedMinutes * Minute,
            MaxPositionTicks = watchedMinutes * Minute,
            Client = "Jellyfin Web",
        };
        configure?.Invoke(play);
        play.Scrobbled = ScrobblePolicy.Evaluate(play, _h.Settings.Current);
        await _h.Store.InsertAsync(play, CancellationToken.None);
    }
}
