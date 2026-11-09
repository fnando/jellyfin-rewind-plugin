# Rewind

A Jellyfin plugin that keeps a durable, per-user play history for every media type, as a
source of truth for scrobblers and trackers (Trakt, Last.fm and the like).

Rewind listens to the server's playback events in-process. It does not depend on webhooks or
on `LastPlayedDate`. Each playback session is written to the plugin's own SQLite database
(`{DataPath}/rewind.db`) the moment playback starts.

## Installation

1. In Jellyfin, go to Dashboard → Plugins → Repositories (Catalog → Repositories on some
   versions) and add:

   ```
   https://github.com/fnando/jellyfin-rewind-plugin/releases/latest/download/manifest.json
   ```

2. Install **Rewind** from the catalog (category "General") and restart Jellyfin.

Requires Jellyfin 10.11 (plugin ABI 12.4). History starts at installation: plays made
before Rewind was installed are not imported.

## How plays are recorded

Every session is stored, one record per user, item and playback session. For example, a
SyncPlay group or a shared session gives each user their own record. Each record keeps:

- **The item as it was when played:** name, type, library, series, season and episode, album,
  disc and track number, artists, genres, year, and provider ids (IMDb, TMDb, TVDb,
  MusicBrainz). The history stays readable after items are renamed or removed. Genres come
  from the album (for tracks) or the series (for episodes) when the item has none of its own,
  which is how Jellyfin usually stores them.
- **How the play went:**
  - start and end time
  - the last position and the furthest position reached
  - the time actually played, with seeks and paused time excluded
  - client, device and play method

Clients are unreliable, so missed and odd events are handled:

- A progress or stop event without a start still creates the record.
- A client that starts, stops at once and starts again with a new session id (a retry or a
  buffering restart) is recorded as one play. Plays that counted as scrobbles are never merged
  this way, however short the track.
- When a client stops without reporting a position, or starts the next item without stopping
  the previous one, the plugin assumes playback continued until that moment. The estimate is
  capped at the item's end, and none is made if it was paused.
- A session that sends nothing for the stale timeout (10 minutes by default) is closed as
  `abandoned`. If its events resume within six hours, the session is reopened.

Plugin updates and server restarts are safe in the middle of playback:

- On shutdown, progress held in memory is written to disk.
- Paused state is stored with each play.
- After a restart, a play picks up where it left off. That includes a track that ended while
  the server was down: it is credited when the client starts the next one.

### Scrobbles

A play counts as a scrobble when it meets the rule for its media type:

| Media | Default rule |
| --- | --- |
| Music | 50% of the runtime or 4 minutes actually played |
| Movies, episodes, videos | furthest position at 80% of the runtime |
| Audiobooks | furthest position at 90% of the runtime |
| Live TV | 5 minutes played |
| Marked as played by the user | always |

Scrobbles are decided from what the plugin measured, not from Jellyfin's "played" flag.
Jellyfin marks anything shorter than five minutes as played once it passes 5%, so a skipped
song would count. The flag is still returned as `playedToCompletion`. There is no minimum
length: an 8-second track played through is a scrobble.

## Configuration

Dashboard → Rewind:

- **Scrobble rules:** for each media type, a percentage of the runtime and/or minutes played.
  The percentage can be measured on the furthest position, so resumed plays count (Trakt
  style), or on the time played in that session (Last.fm style). 0 disables either half.
- **Abandon after:** the stale timeout.
- **Progress write interval:** how often in-progress positions are written. Starts, stops and
  pauses are written immediately.
- **Erase history of deleted users:** off by default.
- **History:** plays and scrobbles per user. The "As of" line says when it was loaded. It
  refreshes every 30 seconds while the page is visible, or on demand with the refresh icon.

Two scheduled tasks (category "Rewind") maintain the recorded history:

- **Re-evaluate scrobbles:** applies the current rules to every recorded play. Run it after
  changing the rules. There is a button for it on the configuration page.
- **Fill in play details:** runs at startup. When an update starts recording a new item field
  (so far: disc number, genres, and genres taken from the album or series), it copies the
  field from the library onto plays recorded before the update.
  - It never overwrites what was recorded at play time.
  - Plays whose item has left the library keep the field empty.
  - Plays still in progress during the update are brought up to date when they close.

Both tasks announce the plays they change on the changes feed.

## API

Except for `/Rewind/Info`, every endpoint needs authentication. Use a user's access token
(`Authorization: MediaBrowser Token="…"`). Results
are scoped to that user. Administrators and server API keys may pass `userId` to read another
user's history; API keys must pass it.

### `GET /Rewind/Info`

Public (no authentication). Identifies the plugin, so a client can check that the server has
Rewind before signing in:

```json
{
  "id": "6bcfddb4-c3fb-48f6-996f-5b02d07e3e62",
  "name": "Rewind",
  "repositoryUrl": "https://github.com/fnando/jellyfin-rewind-plugin",
  "version": "0.0.0.0"
}
```

### `GET /Rewind/History`

Returns plays, newest first. List filters accept comma-separated or repeated values.

| Parameter | |
| --- | --- |
| `types` | `BaseItemKind` names: `Movie,Episode,Audio,AudioBook,MusicVideo,TvChannel`… |
| `mediaTypes` | `Video`, `Audio`, `Photo`, `Book` |
| `itemIds`, `seriesId`, `albumId`, `libraryId` | ids |
| `artist` | artist or album artist (exact, case-insensitive) |
| `genres` | genres (exact, case-insensitive; any of them) |
| `providerId` | `imdb:tt0133093`, `tvdb:81189`… (an episode also matches its series' ids) |
| `startedAfter`, `startedBefore` | ISO 8601 |
| `scrobbled` | `true` (default), `false`, `any` |
| `status` | `active`, `completed`, `abandoned` |
| `source` | `playback`, `manual` (marked played) |
| `client`, `deviceId` | |
| `order` | `desc` (default) or `asc` |
| `limit` | 1–1000, default 100 |
| `cursor` | `nextCursor` from the previous page |

The response is `{ "items": [...], "nextCursor": "…" }`. `nextCursor` is absent on the last
page. Paging is keyset-based, so it stays stable while new plays arrive.

A play (fields without a value are omitted):

```json
{
  "id": "01a1c13028eb7d10b6bcf770ac5fcf93",
  "seq": 14,
  "userId": "0c29eba6-04e4-4d6a-9baa-43cd468d25b4",
  "itemId": "d94eb1e0-57e8-92e3-2d6f-2981834dffd7",
  "itemType": "Audio",
  "mediaType": "Audio",
  "name": "Is It a Lie",
  "libraryId": "7e64e319-657a-9516-ec78-490da03edccb",
  "indexNumber": 1,
  "discNumber": 1,
  "albumId": "860613fd-5b8e-9ed8-cc91-eaba15d8ea49",
  "album": "Plastic Skies",
  "artists": ["Bodyjar"],
  "albumArtists": ["Bodyjar"],
  "productionYear": 2012,
  "genres": ["hardcore punk", "pop punk"],
  "providerIds": { "MusicBrainzRecording": "164bf665-0736-4831-90a6-63abfb5c7ebb", "…": "…" },
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

Episodes also carry `seriesId`, `seriesName`, `seasonNumber` and `seriesProviderIds`.

### `GET /Rewind/History/Changes?since=0`

The incremental sync feed. It returns every play that was created or changed after `since`,
in change order. This includes:

- plays still in progress
- plays whose scrobble status or details changed
- deletions (`"deleted": true`)

Keep `nextSince` and send it on the next call. Keep paging while `hasMore` is `true`. It
accepts `types` and `limit`.

### `GET /Rewind/History/{id}` · `DELETE /Rewind/History/{id}`

Reads or deletes a single play. A deleted play appears as a tombstone in the changes feed.

### `GET /Rewind/Admin/Summary`

Administrators only. Returns the number of plays and scrobbles per user.

## Development

```sh
make check     # build + test
make package   # build/rewind_VERSION.zip, the zip a release ships
```

The **CI** workflow runs `make check` on every pull request and every push to `main`.

### Releasing

Jellyfin installs plugins from a repository manifest. Each version in it points at a zip and
gives that zip's MD5 checksum. Every release attaches its own `manifest.json`, listing all
versions so far, next to `rewind_VERSION.zip`. The repository URL above always resolves to the
newest release's copy. Jellyfin downloads it again on every update check, so servers see a new
version as soon as it is published. The zip holds the plugin DLL only, because Jellyfin already
ships EF Core and SQLite.

1. Run the **Release** workflow (Actions → Release → Run workflow) with a four-part version
   (e.g. `0.1.0.0`) and a changelog. It writes both into `build.yaml`, runs the tests and
   opens a `release/vVERSION` pull request.
2. Merge it. The **Publish** workflow then:
   - builds and tests the merge commit
   - builds the zip
   - writes `manifest.json`: the previous release's manifest with the new version on top
   - creates the `vVERSION` release with both files

`build.yaml` is the only place the version lives; the `Makefile` reads it. Until the first
release it stays `0.0.0.0`.
