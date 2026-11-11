# Rewind

A Jellyfin plugin that keeps a durable, per-user play history for every media
type, as a source of truth for scrobblers and trackers (Trakt, Last.fm and the
like).

Rewind listens to the server's playback events in-process. It does not depend on
webhooks or on `LastPlayedDate`. Each playback session is written to the
plugin's own SQLite database (`{DataPath}/rewind.db`) the moment playback
starts.

## Installation

1. In Jellyfin, go to Dashboard → Plugins → Repositories (Catalog → Repositories
   on some versions) and add:

   ```
   https://github.com/fnando/jellyfin-rewind-plugin/releases/latest/download/manifest.json
   ```

2. Install **Rewind** from the catalog (category "General") and restart
   Jellyfin.

Requires Jellyfin 12.0 or later. History starts at installation: plays made
before Rewind was installed are not imported.

## How plays are recorded

Every session is stored, one record per user, item and playback session. For
example, a SyncPlay group or a shared session gives each user their own record.
Each record keeps:

- **The item as it was when played:** name, type, library, series, season and
  episode, album, disc and track number, artists, genres, year, and provider ids
  (IMDb, TMDb, TVDb, MusicBrainz). The history stays readable after items are
  renamed or removed. Genres come from the album (for tracks) or the series (for
  episodes) when the item has none of its own, which is how Jellyfin usually
  stores them.
- **How the play went:**
  - start and end time
  - the last position and the furthest position reached
  - the time actually played, with seeks and paused time excluded
  - client, device and play method

Clients are unreliable, so missed and odd events are handled:

- A progress or stop event without a start still creates the record.
- A client that starts, stops at once and starts again with a new session id (a
  retry or a buffering restart) is recorded as one play. Plays that counted as
  scrobbles are never merged this way, however short the track.
- When a client stops without reporting a position, or starts the next item
  without stopping the previous one, the plugin assumes playback continued until
  that moment. The estimate is capped at the item's end, and none is made if it
  was paused.
- A session that sends nothing for the stale timeout (10 minutes by default) is
  closed as `abandoned`. If its events resume within six hours, the session is
  reopened.

Clients can also report plays after the fact with
[`POST /Rewind/Scrobbles`](#post-rewindscrobbles), for example plays buffered
while offline. These are stored with the source `scrobble`, so they are kept
apart from what the plugin recorded itself (`playback`) and from items marked
played (`manual`).

Plugin updates and server restarts are safe in the middle of playback:

- On shutdown, progress held in memory is written to disk.
- Paused state is stored with each play.
- After a restart, a play picks up where it left off. That includes a track that
  ended while the server was down: it is credited when the client starts the
  next one.

### Scrobbles

A play counts as a scrobble when it meets the rule for its media type:

| Media                        | Default rule                                    |
| ---------------------------- | ----------------------------------------------- |
| Music                        | 50% of the runtime or 4 minutes actually played |
| Movies, episodes, videos     | furthest position at 80% of the runtime         |
| Audiobooks                   | furthest position at 90% of the runtime         |
| Live TV                      | 5 minutes played                                |
| Marked as played by the user | always                                          |

Scrobbles are decided from what the plugin measured, not from Jellyfin's
"played" flag. Jellyfin marks anything shorter than five minutes as played once
it passes 5%, so a skipped song would count. The flag is still returned as
`playedToCompletion`. There is no minimum length: an 8-second track played
through is a scrobble.

## Configuration

Dashboard → Rewind:

- **Scrobble rules:** for each media type, a percentage of the runtime and/or
  minutes played. The percentage can be measured on the furthest position, so
  resumed plays count (Trakt style), or on the time played in that session
  (Last.fm style). 0 disables either half.
- **Abandon after:** the stale timeout.
- **Progress write interval:** how often in-progress positions are written.
  Starts, stops and pauses are written immediately.
- **Erase history of deleted users:** off by default.
- **History:** plays and scrobbles per user. The "As of" line says when it was
  loaded. It refreshes every 30 seconds while the page is visible, or on demand
  with the refresh icon.

Two scheduled tasks (category "Rewind") maintain the recorded history:

- **Re-evaluate scrobbles:** applies the current rules to every recorded play.
  Run it after changing the rules. There is a button for it on the configuration
  page.
- **Fill in play details:** runs at startup. When an update starts recording a
  new item field (so far: disc number, genres, and genres taken from the album
  or series), it copies the field from the library onto plays recorded before
  the update.
  - It never overwrites what was recorded at play time.
  - Plays whose item has left the library keep the field empty.
  - Plays still in progress during the update are brought up to date when they
    close.

Both tasks announce the plays they change on the changes feed.

## API

Except for `/Rewind/Info`, every endpoint needs authentication. Use a user's
access token (`Authorization: MediaBrowser Token="…"`). Results are scoped to
that user. Administrators and server API keys may pass `userId` to read another
user's history; API keys must pass it. Reporting plays is the exception: it
takes a user's access token only.

### `GET /Rewind/Info`

Public (no authentication). Identifies the plugin, so a client can check that
the server has Rewind before signing in:

```json
{
  "id": "6bcfddb4c3fb48f6996f5b02d07e3e62",
  "name": "Rewind",
  "repositoryUrl": "https://github.com/fnando/jellyfin-rewind-plugin",
  "version": "0.0.0.0"
}
```

### `GET /Rewind/History`

Returns plays, newest first. List filters accept comma-separated or repeated
values.

| Parameter                                     |                                                                             |
| --------------------------------------------- | --------------------------------------------------------------------------- |
| `types`                                       | `BaseItemKind` names: `Movie,Episode,Audio,AudioBook,MusicVideo,TvChannel`… |
| `mediaTypes`                                  | `Video`, `Audio`, `Photo`, `Book`                                           |
| `itemIds`, `seriesId`, `albumId`, `libraryId` | ids                                                                         |
| `artist`                                      | artist or album artist (exact, case-insensitive)                            |
| `genres`                                      | genres (exact, case-insensitive; any of them)                               |
| `providerId`                                  | `imdb:tt0133093`, `tvdb:81189`… (an episode also matches its series' ids)   |
| `startedAfter`, `startedBefore`               | ISO 8601                                                                    |
| `scrobbled`                                   | `true` (default), `false`, `any`                                            |
| `status`                                      | `active`, `completed`, `abandoned`                                          |
| `source`                                      | `playback`, `manual` (marked played), `scrobble` (reported by a client)     |
| `client`, `deviceId`                          |                                                                             |
| `order`                                       | `desc` (default) or `asc`                                                   |
| `limit`                                       | 1–1000, default 100                                                         |
| `cursor`                                      | `nextCursor` from the previous page                                         |

The response is `{ "items": [...], "nextCursor": "…" }`. `nextCursor` is absent
on the last page. Paging is keyset-based, so it stays stable while new plays
arrive.

A play (fields without a value are omitted):

```json
{
  "id": "01a1c13028eb7d10b6bcf770ac5fcf93",
  "seq": 14,
  "userId": "0c29eba604e44d6a9baa43cd468d25b4",
  "itemId": "d94eb1e057e892e32d6f2981834dffd7",
  "itemType": "Audio",
  "mediaType": "Audio",
  "name": "Is It a Lie",
  "libraryId": "7e64e319657a9516ec78490da03edccb",
  "indexNumber": 1,
  "discNumber": 1,
  "albumId": "860613fd5b8e9ed8cc91eaba15d8ea49",
  "album": "Plastic Skies",
  "artists": ["Bodyjar"],
  "albumArtists": ["Bodyjar"],
  "productionYear": 2012,
  "genres": ["hardcore punk", "pop punk"],
  "providerIds": {
    "MusicBrainzRecording": "164bf665-0736-4831-90a6-63abfb5c7ebb",
    "…": "…"
  },
  "startedAt": "2026-11-09T16:42:01Z",
  "lastSeenAt": "2026-11-09T16:44:34Z",
  "endedAt": "2026-11-09T16:44:34Z",
  "status": "completed",
  "source": "playback",
  "positionTicks": 1524933330,
  "maxPositionTicks": 1524933330,
  "runtimeTicks": 1524933330,
  "playedTicks": 1524727604,
  "progress": 100,
  "playedToCompletion": true,
  "scrobbled": true,
  "client": "JellySound",
  "deviceName": "razorblade",
  "deviceId": "027F57EA-F225-474B-89A3-74F13695814C",
  "playMethod": "DirectPlay",
  "updatedAt": "2026-11-09T17:05:12Z"
}
```

Episodes also carry `seriesId`, `seriesName`, `seasonNumber` and
`seriesProviderIds`.

### `GET /Rewind/History/Changes?since=0`

The incremental sync feed. It returns every play that was created or changed
after `since`, in change order. This includes:

- plays still in progress
- plays whose scrobble status or details changed
- deletions (`"deleted": true`)

Keep `nextSince` and send it on the next call. Keep paging while `hasMore` is
`true`. It accepts `types` and `limit`.

### `GET /Rewind/History/{id}` · `DELETE /Rewind/History/{id}`

Reads or deletes a single play. A deleted play appears as a tombstone in the
changes feed.

### `POST /Rewind/Scrobbles`

Reports plays that the server did not see, such as plays a player buffered while
it was offline. They are recorded with `"source": "scrobble"`, the times the
client gives, and a snapshot of the item taken now.

Plays come from a real player, so this endpoint takes a user's access token
only. The plays go to that token's user, and there is no `userId`. API keys get
`403`. Only items the user can access are accepted: any other item is rejected
as not found.

```json
{
  "plays": [
    {
      "itemId": "d94eb1e0-57e8-92e3-2d6f-2981834dffd7",
      "startedAt": "2026-11-09T16:42:01Z",
      "endedAt": "2026-11-09T16:44:34Z",
      "playedTicks": 1524727604,
      "positionTicks": 1524933330,
      "client": "JellySound",
      "deviceName": "razorblade",
      "deviceId": "027F57EA-F225-474B-89A3-74F13695814C"
    }
  ]
}
```

Only `itemId` and `startedAt` are required. A request takes 1–500 plays.

- **`playedTicks`** (the time actually played) defaults to the item's runtime.
  When `endedAt` is given, the default is capped at the time between `startedAt`
  and `endedAt`.
- **`positionTicks`** (the furthest position reached) defaults to the played
  time, capped at the runtime.
- **`endedAt`** defaults to `startedAt` plus the played time.
- **`client`, `deviceName` and `deviceId`** default to the device the token was
  issued to.

Whether a play counts as a scrobble is decided by the same [rules](#scrobbles)
as any other play, from the played time and position. Re-evaluating scrobbles
applies to these plays too.

A new play that counts as a scrobble also updates Jellyfin's own played state of
the item, as if the server had seen the play:

- **Played state:** the item is marked played, and its play count goes up by
  one.
- **Last played date and resume position:** the last played date moves to the
  play's end, and the resume position is cleared. Both happen only if the play
  is newer than the last one Jellyfin knows of. An old offline play still
  counts, but doesn't move the date back or clear where you are in something you
  played later.
- **What doesn't update it:** a duplicate, or a play that doesn't count.

Jellyfin's played state is not Rewind's own data. So re-evaluating scrobbles, or
deleting the play from Rewind, does not undo it.

Reporting is safe to retry. A play with the same item and the same `startedAt`
(to the second) as one already reported is not recorded again. Neither is a play
that overlaps, within two minutes, a scrobble the server already recorded from
its own playback events, so a client may report everything it played. A play the
server saw only in part (for example, the device went offline mid-track) does
not block the report.

Each play is handled on its own. The response has one result per play, in
request order:

```json
{
  "results": [
    {
      "index": 0,
      "status": "created",
      "id": "01a1c13028eb7d10b6bcf770ac5fcf93",
      "scrobbled": true
    },
    {
      "index": 1,
      "status": "duplicate",
      "id": "01a1c0f7aa1e7c40a52d13b1b7c3e0a2",
      "scrobbled": true
    },
    { "index": 2, "status": "rejected", "error": "Item not found." }
  ]
}
```

A play is `rejected` when:

- the item is unknown, is a folder, or is in a library the user can't access
- `startedAt` is missing, not ISO 8601, or more than five minutes in the future
- `endedAt` is before `startedAt`
- a tick value is negative

A client can drop `created` and `duplicate` plays from its queue. The whole
request fails with `400` only when `plays` is missing, empty or has more than
500 entries.

### `GET /Rewind/Admin/Summary`

Administrators only. Returns the number of plays and scrobbles per user.

## Reporting offline plays (for player developers)

A player keeps reporting playback to Jellyfin as it does today. Rewind records
it from the server's events. `POST /Rewind/Scrobbles` is only the fallback for
plays the server could not see. This section describes how a player falls back
to it.

### 1. Check for Rewind

At sign-in or on reconnect, call `GET /Rewind/Info`. It needs no authentication.

- **`200`:** Rewind is installed. Queue offline plays and submit them as
  described below.
- **`404`:** Rewind is not installed. Don't queue plays for it.

### 2. Report live while online

While the server is reachable, use Jellyfin's normal playback reporting:

- `POST /Sessions/Playing` when playback starts
- `POST /Sessions/Playing/Progress` while it plays
- `POST /Sessions/Playing/Stopped` when it ends

Rewind turns these into plays with `"source": "playback"`. Don't also send these
plays to `/Rewind/Scrobbles`.

### 3. Queue every play whose stop report didn't succeed

The rule: **a play goes in the queue unless its `POST /Sessions/Playing/Stopped`
got a successful response.** That covers:

- **Played entirely offline:** none of the reports reached the server.
- **Connection lost mid-play:** the server saw the start, then nothing. It
  closes that play as `abandoned` after the stale timeout, with only the time it
  saw.
- **The stop report failed:** the play ended, but the server never learned how.

In the last two cases the server already has a partial play, and the report adds
the full one:

- **The partial play counted as a scrobble:** the report comes back `duplicate`.
  Rewind finds the scrobble the server recorded within two minutes of the
  reported play.
- **It didn't count:** the report is recorded as a second play. Only the
  reported one counts, and the default history shows scrobbles only.

Queue plays however short they are, including skipped tracks. The server applies
the scrobble rules and decides whether each one counts.

### 4. What to store for each play

Record these when the play ends, and never change them afterwards:

| Field           | Value                                                                             |
| --------------- | --------------------------------------------------------------------------------- |
| `itemId`        | The Jellyfin item id.                                                             |
| `startedAt`     | When playback started, in UTC, from the device clock.                             |
| `endedAt`       | When playback ended, in UTC.                                                      |
| `playedTicks`   | The time actually played, without pauses and seeks (1 second = 10,000,000 ticks). |
| `positionTicks` | The furthest position reached.                                                    |

Send `playedTicks` whenever the player can measure it. Without it, Rewind
assumes the item was played through, capped at the time between `startedAt` and
`endedAt`. A skipped track would then count as a full play.

Store `startedAt` once and send exactly that value every time. Together with the
item, it is what makes a retry safe (to the second). A play with a new
`startedAt` is a new play: playing the same track twice is meant to give two
plays.

`client`, `deviceName` and `deviceId` are optional. By default they are the
device the access token belongs to. Set them only when the play happened on
another device, such as a watch syncing through a phone.

Keep the device clock in sync with the server's clock:

- The duplicate check compares the reported times with the server's, within two
  minutes.
- A `startedAt` more than five minutes in the future is rejected.

### 5. Submit the queue

When the server is reachable again:

1. Send the queue oldest first, in batches of at most 500 plays, to
   `POST /Rewind/Scrobbles`. Authenticate with the access token of the user who
   played. The plays go to that user; **API keys are refused** (`403`).
2. Go through `results`. Each `index` is the play's position in the batch.
   - **`created` or `duplicate`:** the server has the play. Remove it from the
     queue.
   - **`rejected`:** remove it from the queue too. Retrying won't change the
     answer, because the problem is in the play itself: an item no longer in the
     library or one the user can't access, a start in the future, invalid times.
3. On anything but a `200`, keep the whole batch and act on the status:

   | Status               | Meaning                                       | What to do                                                                     |
   | -------------------- | --------------------------------------------- | ------------------------------------------------------------------------------ |
   | network error, `5xx` | the server didn't process it                  | Retry later with the same batch. Plays already recorded come back `duplicate`. |
   | `401`                | the token expired or was revoked              | Sign in again, then retry.                                                     |
   | `403`                | not a user's access token                     | Use the user's token.                                                          |
   | `400`                | the batch is empty or has more than 500 plays | Fix the batch.                                                                 |
   | `404`                | Rewind was uninstalled                        | Stop submitting.                                                               |

Live reporting and submitting the queue are independent of each other. Neither
has to wait for the other.

### 6. Don't mark items played as well

Reporting a play that counts already updates Jellyfin's played state: played,
play count and last played date. The player has nothing more to sync.

Don't also mark the item played (`POST /UserPlayedItems/{itemId}`). Rewind
records a mark-played as a play of its own, with the source `manual`, so the
play would be recorded twice. When Rewind is installed, report offline plays
here only.

## Development

```sh
make check     # build + test
make package   # build/rewind_VERSION.zip, the zip a release ships
```

The **CI** workflow runs `make check` on every pull request and every push to
`main`.

### Releasing

Jellyfin installs plugins from a repository manifest. Each version in it points
at a zip and gives that zip's MD5 checksum. Every release attaches its own
`manifest.json`, listing all versions so far, next to `rewind_VERSION.zip`. The
repository URL above always resolves to the newest release's copy. Jellyfin
downloads it again on every update check, so servers see a new version as soon
as it is published. The zip holds the plugin DLL only, because Jellyfin already
ships EF Core and SQLite.

1. Run the **Release** workflow (Actions → Release → Run workflow) with a
   four-part version (e.g. `0.1.0.0`) and a changelog. It writes both into
   `build.yaml`, runs the tests and opens a `release/vVERSION` pull request.
2. Merge it. The **Publish** workflow then:
   - builds and tests the merge commit
   - builds the zip
   - writes `manifest.json`: the previous release's manifest with the new
     version on top
   - creates the `vVERSION` release with both files

`build.yaml` is the only place the version lives; the `Makefile` reads it. Until
the first release it stays `0.0.0.0`.
