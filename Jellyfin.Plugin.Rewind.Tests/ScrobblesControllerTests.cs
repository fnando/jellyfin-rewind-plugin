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
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Rewind.Tests;

public sealed class ScrobblesControllerTests : IDisposable
{
    private readonly Harness _h = new();
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<IUserDataManager> _userData = new();

    // Jellyfin's played state of the item being reported, as GetUserData returns it.
    private readonly UserItemData _state = new() { Key = "state" };
    private readonly Guid _alice = Guid.NewGuid();
    private readonly Guid _bob = Guid.NewGuid();
    private readonly Audio _song = new() { Id = Guid.NewGuid(), Name = "Is It a Lie", RunTimeTicks = Seconds(180) };
    private readonly Audio _other = new() { Id = Guid.NewGuid(), Name = "Next", RunTimeTicks = Seconds(200) };
    private readonly MusicAlbum _album = new() { Id = Guid.NewGuid(), Name = "Plastic Skies" };

    // In the library, but not in one the users can access.
    private readonly Audio _hidden = new() { Id = Guid.NewGuid(), Name = "Hidden", RunTimeTicks = Seconds(180) };

    public ScrobblesControllerTests()
    {
        _users.Setup(u => u.GetUserById(It.IsAny<Guid>())).Returns((User?)null);
        _users.Setup(u => u.GetUserById(_alice)).Returns(new User("alice", "default", "default"));
        _users.Setup(u => u.GetUserById(_bob)).Returns(new User("bob", "default", "default"));
        foreach (var item in new BaseItem[] { _song, _other, _album, _hidden })
        {
            _h.Library.Setup(l => l.GetItemById(item.Id)).Returns(item);
        }

        _userData.Setup(u => u.GetUserData(It.IsAny<User>(), It.IsAny<BaseItem>())).Returns(_state);

        // The access-checked lookup the endpoint uses.
        _h.Library.Setup(l => l.GetItemById<BaseItem>(It.IsAny<Guid>(), It.IsAny<User>())).Returns((BaseItem?)null);
        foreach (var item in new BaseItem[] { _song, _other, _album })
        {
            _h.Library.Setup(l => l.GetItemById<BaseItem>(item.Id, It.IsAny<User>())).Returns(item);
        }
    }

    // Offline plays happened yesterday; they are reported now.
    private static DateTime Yesterday => Harness.T0.AddDays(-1);

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task PastPlay_IsRecordedAsScrobble_WithItsOwnTimes()
    {
        var response = await Post(As(_alice), Entry(_song, Yesterday, endedAt: Yesterday.AddSeconds(180), played: Seconds(180), client: "JellySound"));

        var result = Assert.Single(response.Results);
        Assert.Equal("created", result.Status);
        Assert.True(result.Scrobbled);

        var play = await _h.Store.GetAsync(_alice, result.Id!, CancellationToken.None);
        Assert.NotNull(play);
        Assert.Equal(PlaySource.Scrobble, play.Source);
        Assert.Equal(PlayStatus.Completed, play.Status);
        Assert.Equal(Yesterday, play.StartedAt);
        Assert.Equal(Yesterday.AddSeconds(180), play.EndedAt);
        Assert.Equal("Is It a Lie", play.Name);
        Assert.Equal("Audio", play.ItemType);
        Assert.Equal("JellySound", play.Client);
        Assert.True(play.PlayedToCompletion);
        Assert.Equal("scrobble", HistoryEntry.From(play).Source);

        var (changes, _) = await _h.Store.ChangesAsync(_alice, 0, 100, null, CancellationToken.None);
        Assert.Equal(result.Id, Assert.Single(changes).Id);
    }

    [Fact]
    public async Task ScrobbleRules_Decide_FromThePlayedTime()
    {
        var response = await Post(
            As(_alice),
            Entry(_song, Yesterday, played: Seconds(30)),
            Entry(_other, Yesterday.AddMinutes(10)));

        Assert.False(response.Results[0].Scrobbled);
        Assert.True(response.Results[1].Scrobbled);

        // Without a played time, the item is taken as played through.
        var full = await _h.Store.GetAsync(_alice, response.Results[1].Id!, CancellationToken.None);
        Assert.Equal(Seconds(200), full!.PlayedTicks);
        Assert.Equal(Seconds(200), full.MaxPositionTicks);
        Assert.Equal(Yesterday.AddMinutes(10).AddSeconds(200), full.EndedAt);
    }

    [Fact]
    public async Task WithoutPlayedTime_TheReportedSpanCapsTheRuntime()
    {
        var response = await Post(As(_alice), Entry(_song, Yesterday, endedAt: Yesterday.AddSeconds(40)));

        var play = await _h.Store.GetAsync(_alice, Assert.Single(response.Results).Id!, CancellationToken.None);
        Assert.Equal(Seconds(40), play!.PlayedTicks);
        Assert.False(play.Scrobbled);
    }

    [Fact]
    public async Task Resend_ReturnsTheSameRecord()
    {
        var first = Assert.Single((await Post(As(_alice), Entry(_song, Yesterday, rawStart: "2026-11-08T20:00:00Z"))).Results);
        var again = Assert.Single((await Post(As(_alice), Entry(_song, Yesterday, rawStart: "2026-11-08T20:00:00.4567+00:00"))).Results);

        Assert.Equal("created", first.Status);
        Assert.Equal("duplicate", again.Status);
        Assert.Equal(first.Id, again.Id);
        Assert.Single(await All(_alice));
    }

    [Fact]
    public async Task SameItemAtAnotherTime_IsAnotherPlay()
    {
        await Post(As(_alice), Entry(_song, Yesterday), Entry(_song, Yesterday.AddMinutes(3)));

        Assert.Equal(2, (await All(_alice)).Count);
    }

    [Fact]
    public async Task OverlappingScrobbledPlayback_IsADuplicate()
    {
        var recorded = await Recorded(_song, Yesterday.AddSeconds(5), scrobbled: true);

        var result = Assert.Single((await Post(As(_alice), Entry(_song, Yesterday, played: Seconds(180)))).Results);

        Assert.Equal("duplicate", result.Status);
        Assert.Equal(recorded.Id, result.Id);
        Assert.Single(await All(_alice));
    }

    [Fact]
    public async Task OverlappingUnscrobbledPlayback_DoesNotBlockTheReport()
    {
        // The server saw the start, then the device went offline.
        await Recorded(_song, Yesterday, scrobbled: false);

        var result = Assert.Single((await Post(As(_alice), Entry(_song, Yesterday, played: Seconds(180)))).Results);

        Assert.Equal("created", result.Status);
        Assert.Equal(2, (await All(_alice)).Count);
    }

    [Fact]
    public async Task PlaybackOfAnotherUser_IsNotAnOverlap()
    {
        await Recorded(_song, Yesterday, scrobbled: true, userId: _bob);

        Assert.Equal("created", Assert.Single((await Post(As(_alice), Entry(_song, Yesterday))).Results).Status);
    }

    [Fact]
    public async Task BadEntries_AreRejected_WithoutFailingTheBatch()
    {
        var response = await Post(
            As(_alice),
            Entry(_song, Yesterday),
            new ScrobbleRequestPlay { ItemId = Guid.NewGuid().ToString(), StartedAt = "2026-11-08T20:00:00Z" },
            Entry(_album, Yesterday),
            Entry(_other, Harness.T0.AddHours(1)),
            Entry(_other, Yesterday, endedAt: Yesterday.AddMinutes(-1)),
            new ScrobbleRequestPlay { ItemId = "nope", StartedAt = "2026-11-08T20:00:00Z" },
            new ScrobbleRequestPlay { ItemId = _other.Id.ToString(), StartedAt = "yesterday" },
            Entry(_other, Yesterday, played: -1),
            Entry(_other, Yesterday.AddMinutes(5)));

        Assert.Equal(
            new[] { "created", "rejected", "rejected", "rejected", "rejected", "rejected", "rejected", "rejected", "created" },
            response.Results.Select(r => r.Status));
        Assert.Equal(Enumerable.Range(0, 9), response.Results.Select(r => r.Index));
        Assert.All(response.Results.Where(r => r.Status == "rejected"), r => Assert.NotNull(r.Error));
        Assert.Equal(2, (await All(_alice)).Count);
    }

    [Fact]
    public async Task SlightlyFutureStart_IsAcceptedForClockSkew()
    {
        var result = Assert.Single((await Post(As(_alice), Entry(_song, Harness.T0.AddMinutes(2)))).Results);

        Assert.Equal("created", result.Status);
    }

    [Fact]
    public async Task EmptyOrOversizedBatch_IsABadRequest()
    {
        Assert.IsType<BadRequestObjectResult>((await Call(As(_alice), new ScrobbleRequest())).Result);
        Assert.IsType<BadRequestObjectResult>((await Call(As(_alice), new ScrobbleRequest { Plays = [] })).Result);

        var tooMany = Enumerable.Range(0, ScrobblesController.MaxPlays + 1).Select(n => Entry(_song, Yesterday.AddMinutes(n * 4))).ToList();
        Assert.IsType<BadRequestObjectResult>((await Call(As(_alice), new ScrobbleRequest { Plays = tooMany })).Result);
        Assert.Empty(await All(_alice));
    }

    [Fact]
    public async Task ApiKey_IsRefused()
    {
        var result = Assert.IsType<ObjectResult>((await Call(ApiKey(), Request(Entry(_song, Yesterday)))).Result);

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Empty(await All(_alice));
    }

    [Fact]
    public async Task TokenForAnUnknownUser_IsRefused()
    {
        var result = Assert.IsType<ObjectResult>((await Call(As(Guid.NewGuid()), Request(Entry(_song, Yesterday)))).Result);

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    [Fact]
    public async Task Plays_GoToTheTokensUser()
    {
        await Post(As(_bob), Entry(_song, Yesterday));
        await Post(As(_alice, admin: true), Entry(_other, Yesterday));

        Assert.Equal("Is It a Lie", Assert.Single(await All(_bob)).Name);
        Assert.Equal("Next", Assert.Single(await All(_alice)).Name);
    }

    [Fact]
    public async Task ItemTheUserCannotAccess_IsNotFound()
    {
        var response = await Post(As(_alice), Entry(_hidden, Yesterday), Entry(_song, Yesterday));

        Assert.Equal(new[] { "rejected", "created" }, response.Results.Select(r => r.Status));
        Assert.Equal("Item not found.", response.Results[0].Error);
        Assert.Equal("Is It a Lie", Assert.Single(await All(_alice)).Name);
    }

    [Fact]
    public async Task ClientAndDevice_DefaultToTheCallersSession()
    {
        var caller = As(_alice, claims: [new Claim("Jellyfin-Client", "Finamp"), new Claim("Jellyfin-Device", "iPhone"), new Claim("Jellyfin-DeviceId", "abc")]);

        var response = await Post(caller, Entry(_song, Yesterday), Entry(_other, Yesterday.AddMinutes(5), client: "JellySound"));

        var plays = (await All(_alice)).OrderBy(p => p.StartedAt).ToList();
        Assert.Equal(new[] { "Finamp", "JellySound" }, plays.Select(p => p.Client));
        Assert.All(plays, p => Assert.Equal("iPhone", p.DeviceName));
        Assert.All(plays, p => Assert.Equal("abc", p.DeviceId));
        Assert.Equal(2, response.Results.Count);
    }

    [Fact]
    public async Task NewScrobble_UpdatesJellyfinsPlayedState()
    {
        _state.PlayCount = 2;
        _state.LastPlayedDate = Yesterday.AddDays(-7);
        _state.PlaybackPositionTicks = Seconds(42);

        await Post(As(_alice), Entry(_song, Yesterday, endedAt: Yesterday.AddSeconds(180)));

        _userData.Verify(u => u.SaveUserData(It.IsAny<User>(), _song, _state, UserDataSaveReason.PlaybackFinished, It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(_state.Played);
        Assert.Equal(3, _state.PlayCount);
        Assert.Equal(Yesterday.AddSeconds(180), _state.LastPlayedDate);
        Assert.Equal(0, _state.PlaybackPositionTicks);
    }

    [Fact]
    public async Task OlderScrobble_CountsWithoutMovingLastPlayedBack()
    {
        // Jellyfin saw a later play (and a resume point) after the offline one.
        _state.PlayCount = 1;
        _state.LastPlayedDate = Harness.T0.AddHours(-1);
        _state.PlaybackPositionTicks = Seconds(42);

        await Post(As(_alice), Entry(_song, Yesterday));

        Assert.True(_state.Played);
        Assert.Equal(2, _state.PlayCount);
        Assert.Equal(Harness.T0.AddHours(-1), _state.LastPlayedDate);
        Assert.Equal(Seconds(42), _state.PlaybackPositionTicks);
    }

    [Fact]
    public async Task PlayThatDoesNotCount_OrDuplicate_LeavesPlayedStateAlone()
    {
        await Post(As(_alice), Entry(_song, Yesterday, played: Seconds(10)));
        await Post(As(_alice), Entry(_other, Yesterday.AddMinutes(10)));
        await Post(As(_alice), Entry(_other, Yesterday.AddMinutes(10)));
        await Recorded(_song, Yesterday.AddHours(2), scrobbled: true);
        await Post(As(_alice), Entry(_song, Yesterday.AddHours(2)));

        // Only the first report of the full play of _other counted.
        _userData.Verify(u => u.SaveUserData(It.IsAny<User>(), It.IsAny<BaseItem>(), It.IsAny<UserItemData>(), It.IsAny<UserDataSaveReason>(), It.IsAny<CancellationToken>()), Times.Once);
        _userData.Verify(u => u.SaveUserData(It.IsAny<User>(), _other, It.IsAny<UserItemData>(), It.IsAny<UserDataSaveReason>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FailingPlayedStateUpdate_StillRecordsThePlay()
    {
        _userData
            .Setup(u => u.SaveUserData(It.IsAny<User>(), It.IsAny<BaseItem>(), It.IsAny<UserItemData>(), It.IsAny<UserDataSaveReason>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("database is locked"));

        var result = Assert.Single((await Post(As(_alice), Entry(_song, Yesterday))).Results);

        Assert.Equal("created", result.Status);
        Assert.Single(await All(_alice));
    }

    [Fact]
    public async Task SourceFilter_FindsReportedPlays()
    {
        await Recorded(_other, Yesterday.AddHours(-2), scrobbled: true);
        await Post(As(_alice), Entry(_song, Yesterday));

        var (items, _) = await _h.Store.QueryAsync(new HistoryQuery { UserId = _alice, Sources = [PlaySource.Scrobble] }, CancellationToken.None);

        Assert.Equal("Is It a Lie", Assert.Single(items).Name);
    }

    private static long Seconds(int seconds) => TimeSpan.FromSeconds(seconds).Ticks;

    private static ScrobbleRequestPlay Entry(BaseItem item, DateTime startedAt, DateTime? endedAt = null, long? played = null, string? client = null, string? rawStart = null)
        => new()
        {
            ItemId = item.Id.ToString(),
            StartedAt = rawStart ?? startedAt.ToString("O"),
            EndedAt = endedAt?.ToString("O"),
            PlayedTicks = played,
            Client = client,
        };

    private static ScrobbleRequest Request(params ScrobbleRequestPlay[] plays) => new() { Plays = plays };

    private static ClaimsPrincipal ApiKey()
        => new(new ClaimsIdentity(
            new[]
            {
                new Claim(RequestUser.UserIdClaim, Guid.Empty.ToString()),
                new Claim(ClaimTypes.Role, RequestUser.AdministratorRole),
                new Claim(RequestUser.IsApiKeyClaim, "true"),
            },
            "Custom"));

    private static ClaimsPrincipal As(Guid userId, bool admin = false, Claim[]? claims = null)
        => new(new ClaimsIdentity(
            new[]
            {
                new Claim(RequestUser.UserIdClaim, userId.ToString()),
                new Claim(ClaimTypes.Role, admin ? RequestUser.AdministratorRole : "User"),
            }.Concat(claims ?? []),
            "Custom"));

    private ScrobblesController Controller(ClaimsPrincipal user)
        => new(_h.Tracker, _h.Library.Object, _users.Object, _userData.Object, _h.Time, NullLogger<ScrobblesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } },
        };

    private Task<ActionResult<ScrobbleResponse>> Call(ClaimsPrincipal user, ScrobbleRequest request)
        => Controller(user).PostScrobbles(request, CancellationToken.None);

    private async Task<ScrobbleResponse> Post(ClaimsPrincipal user, params ScrobbleRequestPlay[] plays)
        => Assert.IsType<ScrobbleResponse>((await Call(user, Request(plays))).Value);

    private async Task<IReadOnlyList<PlayRecord>> All(Guid userId)
        => (await _h.Store.QueryAsync(new HistoryQuery { UserId = userId, Limit = 1000 }, CancellationToken.None)).Items;

    // A play the server recorded from its own playback events.
    private async Task<PlayRecord> Recorded(Audio item, DateTime startedAt, bool scrobbled, Guid? userId = null)
    {
        var played = scrobbled ? item.RunTimeTicks!.Value : Seconds(20);
        var play = new PlayRecord
        {
            Id = Guid.CreateVersion7().ToString("N"),
            UserId = userId ?? _alice,
            ItemId = item.Id,
            SessionKey = Guid.NewGuid().ToString("N"),
            ItemType = "Audio",
            MediaType = "Audio",
            Name = item.Name,
            Source = PlaySource.Playback,
            Status = PlayStatus.Completed,
            StartedAt = startedAt,
            LastSeenAt = startedAt + TimeSpan.FromTicks(played),
            EndedAt = startedAt + TimeSpan.FromTicks(played),
            RuntimeTicks = item.RunTimeTicks,
            PlayedTicks = played,
            PositionTicks = played,
            MaxPositionTicks = played,
            Scrobbled = scrobbled,
        };
        await _h.Store.InsertAsync(play, CancellationToken.None);
        return play;
    }
}
