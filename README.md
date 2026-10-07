# PlayerDetailSync

PlayerDetailSync exposes live player data over an authenticated server HTTP API. Connected-player identity and position are server-observed; status effects and food timers require the client companion on each reporting player's game.

## Current API

`GET /api/v1/health` confirms the listener is running and returns the current snapshot time and connected-player count:

```json
{
  "status": "ok",
  "updatedAtUtc": "2026-10-06T12:00:00.0000000Z",
  "connectedPlayerCount": 2
}
```

`GET /api/v1/players` returns a lightweight list of all cached players, including offline players. Both list and detail endpoints read memory only: they never trigger game capture or RPCs.

```json
{
  "updatedAtUtc": "2026-10-06T12:00:00.0000000Z",
  "players": [
    {
      "playerId": "1829075840",
      "steamUserId": "76561198000000000",
      "name": "Player",
      "online": true,
      "lastSeenUtc": "2026-10-06T12:00:00.0000000Z",
      "position": { "x": 12.5, "y": 4.0, "z": -30.25 }
    }
  ]
}
```

`GET /api/v1/players/{playerId}` returns one player's detailed character snapshot:

```json
{
  "playerId": "1829075840",
  "steamUserId": "76561198000000000",
  "name": "Player",
  "online": true,
  "lastSeenUtc": "2026-10-06T12:00:00.0000000Z",
  "characterDataAvailable": true,
  "characterDataAgeSeconds": 0.4,
  "position": { "x": 12.5, "y": 4.0, "z": -30.25 },
  "statuses": [{ "name": "Rested", "remainingSeconds": 1200, "iconKey": "status_rested", "hasDuration": true }],
  "foods": [{ "name": "Cooked Meat", "remainingSeconds": 1500, "iconKey": "CookedDeerMeat", "hasDuration": true }],
  "stats": { "health": 125, "maxHealth": 150, "stamina": 87, "maxStamina": 120, "eitr": 0, "maxEitr": 0 },
  "equipment": [{ "name": "Iron Helmet", "slot": "Helmet", "quality": 2, "iconKey": "ironhelmet" }],
  "followers": [{ "followerId": "123:456", "name": "Fang", "species": "Wolf", "level": 2, "position": { "x": 10, "y": 4, "z": -28 } }]
}
```

### Followers

`GET /api/v1/players/{playerId}/followers` returns follower summaries plus the player's `online`, `lastSeenUtc`, `characterDataAvailable`, and `characterDataAgeSeconds` metadata. Player details include the same `followers` summaries; the lightweight players list is unchanged.

`GET /api/v1/players/{playerId}/followers/{followerId}` returns that metadata and a detailed `follower` object:

```json
{
  "playerId": "1829075840",
  "online": true,
  "lastSeenUtc": "2026-10-06T12:00:00.0000000Z",
  "characterDataAvailable": true,
  "characterDataAgeSeconds": 0.4,
  "follower": {
    "followerId": "123:456",
    "name": "Fang",
    "species": "Wolf",
    "position": { "x": 10, "y": 4, "z": -28 },
    "level": 2,
    "tamed": true,
    "hungry": false,
    "alerted": false,
    "stats": { "health": 120, "maxHealth": 160 },
    "statuses": [{ "name": "Protection", "remainingSeconds": 30, "hasDuration": true, "iconKey": "shield", "shieldRemainingAbsorption": 150, "shieldMaxAbsorption": 200 }]
  }
}
```

`name` is the game's display name, including a pet's custom name such as Thora or Rex; `species` remains the localized creature type. Custom display names are not localized again by the API.

Use the exact `followerId` returned by the API, URL-encoded as one path segment. It is Valheim's composite network-object ID (`namespace:objectCounter`), not an array index, Steam ID, or pet owner's player ID. Every client and server sees the same ID for the same network object; list reordering and ownership changes do not change it. It normally survives a player disconnect/reconnect while the world keeps running, but the current game reconstructs IDs on world load. Server/world restarts may therefore change or reuse IDs, and newly summoned/recreated creatures get new IDs. Do not use this value as a permanent pet identity or join historical data across world reloads. A durable identity would require a separate mod-assigned identifier persisted on the creature. Missing players or followers return `404`. All follower routes require the existing `X-API-Password` header.

Only loaded, living creatures whose AI follow target is the player are included, including commanded tame pets and following summons. Nearby pets told to stay are excluded. Accepted client reports are preferred over server-side character copies because the follow target and status effects can be client-owned. Install this updated DLL on both server and clients. Modded companions with a different AI/follow mechanism or resources not exposed by Valheim's Character API are not guaranteed to appear.

Follower stats contain only `health` and `maxHealth`. Starting with version 1.0.0, `stamina`, `maxStamina`, `eitr`, and `maxEitr` are removed from followers; player stats are unchanged. `hungry` is `null` without a Tameable component. Protection bubbles derived from Valheim's shield effect include remaining and maximum damage absorption; other effects omit those fields. Status countdowns are aged and clamped between online reports, but health, shield absorption, position, and follower membership are last observed, not simulated. Zero remaining time does not assert a status is still active; wait for the next report to confirm removal. Offline records preserve last-known followers and timers, so check the metadata before treating the list as current. Reports allow up to 64 followers and 64 effects per follower, with a 262,144-character payload limit; oversized or invalid reports are rejected.

Automatic collection runs at the server-synchronized `Refresh Interval` (default 5 seconds, configurable from 1-60 seconds). Existing configs retain their saved interval. Joining/rejoining triggers an immediate refresh; missing-report retries back off through 1, 2, 4, 8, 16, and 30 seconds, then remain at 30 seconds until a report is accepted. Retry state resets on acceptance or disconnect, and client captures remain limited to once per second. Routine refresh/report logs are debug-only; rejected reports log the failing field at most once per 10 seconds. Players are captured only after a stable character ID is available from their server-side character or an accepted client report. Temporary network IDs are never published or cached. These IDs are character IDs, not Steam IDs.

The server saves `JoksterCube.PlayerDetailSync.players.json` beside its BepInEx config. Cache serialization and file writes run on a background worker with a single pending save; the file is loaded at startup with every restored player initially offline. Do not share the cache publicly: it contains player positions and character data. A failed save is logged; abrupt termination can lose the most recent pending update. The cache belongs to this server installation; do not reuse it for another world.

The cache and API keep exactly one record per character ID, whether online or offline. Reconnecting updates that record rather than adding another. Duplicate IDs in a saved cache are collapsed to the most recently seen record at startup. Legacy caches without the new `identityVersion` marker are cleared once because they may contain temporary network IDs; old offline history is discarded and records rebuild as players are identified. A joining player with no known character ID is omitted until identification succeeds, so the health endpoint's connected-player count covers identified players only.

`steamUserId` is the Steam account ID observed by the server on native Steam connections or Steam players using crossplay, serialized as a string in both list and detail responses and retained in the cache. Crossplay identities are parsed using Valheim's platform-ID parser; other platforms are not treated as Steam users. It is an empty string when unavailable, including non-Steam players. One Steam account can have multiple characters, and names are not unique: distinct character IDs remain separate even when their names or Steam IDs match.

Offline records retain last-known vitals, equipment, foods, and statuses rather than implying those values are current. `online` indicates connection state, `lastSeenUtc` records the most recent online sample, and `characterDataAgeSeconds` indicates detail age (`-1` if none was received). Online timed effects are adjusted between client reports; offline timed effects remain as last observed. If a report has never arrived, `characterDataAvailable` is false; polling the API cannot force data from an absent or nonreporting client.

Effects and foods include `hasDuration` and `remainingSeconds`. Untimed statuses have `hasDuration: false` and `remainingSeconds: 0`; zero does not mean they have expired. Timed statuses and foods have `hasDuration: true`, with expired countdowns clamped to zero. Statuses, foods, and gear include an `iconKey` based on the game's sprite name. The key is an identifier, not image bytes. A dependent client mod can resolve it to a local Unity sprite:

```csharp
using JoksterCube.PlayerDetailSync.Api;
using UnityEngine;

if (PlayerDetailSyncApi.TryGetIcon(iconKey, out Sprite icon))
{
  // Use icon in the dependent mod's client UI.
}
```

Declare `JoksterCube.PlayerDetailSync` as a BepInEx dependency and use the same mod version as the server. `characterDataAvailable` indicates whether character data is available, either from a server-side character object or a recent client report. If false, install/enable the mod on that player's client; their status, food, stat, and equipment data will otherwise be unavailable.

## Installation and access

Install the mod on the dedicated server (or the server host) with BepInEx. To include status effects and food timers, install and enable the same mod version on each player's client. Clients send their own active effects to the server over Valheim's authenticated routed RPC; they do not need the HTTP API password. The active config on the configured Compose server is `valheim-data/config/bepinex/JoksterCube.PlayerDetailSync.cfg` on the host, mounted inside the container at `/config/bepinex/JoksterCube.PlayerDetailSync.cfg`.

Server and clients must run the exact same PlayerDetailSync version. The version handshake disconnects clients that report a different version; peers that do not answer within 15 seconds are also disconnected. ServerSync's minimum version is set to the current version as a second check. `Enabled` and `Refresh Interval` are synchronized from the server and enforced on clients. Refreshes are clamped to one per second or slower to limit gameplay overhead. `Lock Configuration` restricts changes to synchronized entries to server admins. `Bind Address`, `Port`, and `Password` are server-local and are never synchronized. ServerSync is bundled inside PlayerDetailSync, so no separate ServerSync install is required.

The API binds to `127.0.0.1:8785` by default, so it is reachable only from the server machine. Set a non-empty `Password` in the config to enable the API; with the default empty password, the listener will not start. Example request from PowerShell:

```powershell
$password = "<Password from the server config>"
Invoke-RestMethod -Headers @{ "X-API-Password" = $password } -Uri "http://127.0.0.1:8785/api/v1/players"
```

To allow Postman or another remote integration to connect, set `Bind Address` to `0.0.0.0` in the server config. Then create `docker-compose.override.yml` beside `docker-compose.yml` and publish the API port on the server's reachable address:

```yaml
services:
  valheim:
    ports:
      - "192.0.2.10:8785:8785/tcp" # Replace with the server's reachable IP.
```

Recreate the service with `docker compose up -d --force-recreate`. Restrict TCP `8785` in the server firewall to trusted client IPs. Send the password in the `X-API-Password` header. Plain HTTP does not encrypt the password in transit; use a trusted network or put the API behind an HTTPS reverse proxy. Protect the password and do not expose the port to untrusted networks. On Windows, `HttpListener` may require a URL reservation for the selected prefix.

Configuration changes take effect live. Changing the bind address, port, or password restarts the listener.

## Build and server deployment

Building the project installs the final `PlayerDetailSync.dll` into the local game's `BepInEx/plugins` directory, updates the server's `Password` entry, and deploys the same DLL to the configured `valheim` Compose service. The server plugin is installed at `/config/bepinex/plugins/PlayerDetailSync.dll`, and its config is `/config/bepinex/JoksterCube.PlayerDetailSync.cfg`; both are mounted from the configured server data directory. The Valheim game path, local plugin directory, remote host, and Compose directory are stored in the ignored local props file. Close Valheim before building if Windows prevents replacing a DLL currently loaded by the game. Other players still need to install the same built/released DLL on their clients to report statuses and foods.

Set `DeployApiPassword` once in the ignored `environment.local.props` file:

```xml
<Project>
  <PropertyGroup>
    <ValheimGamePath>C:\Path\To\Valheim</ValheimGamePath>
    <DeployApiPassword>your-testing-password</DeployApiPassword>
    <DeployRemoteHost>cube@your-server-address</DeployRemoteHost>
    <DeployComposeDirectory>~/test-server</DeployComposeDirectory>
    <DeployLocalPluginDirectory>C:\Path\To\Valheim\BepInEx\plugins</DeployLocalPluginDirectory>
  </PropertyGroup>
</Project>
```

The build fails deployment if it is empty, updates only the server config's `[1 - HTTP API] Password` entry, and preserves the other server settings. Do not commit or share this local file. SSH key authentication must work without an interactive prompt, and the server account must be able to use Docker. To build without deploying, run `dotnet build PlayerDetailSync.sln -p:DeployPlugin=false`.

After installing the DLL and updating the password, the build automatically runs `docker compose up -d --force-recreate` from the configured Compose directory. This recreates the Compose project so the server starts with the new DLL and config.

## Server smoke test

Build-only regression: `dotnet build -p:DeployPlugin=false`, then `pwsh -File .\Tests\Test-PlayerCache.ps1`. This checks save/load, offline retention, reconnect merging, untimed status conversion, older cache compatibility, follower report validation, timer/shield preservation, and follower HTTP routes using a temporary local listener without contacting the game server.

With the mod running and a non-empty `DeployApiPassword` in `environment.local.props`, run `pwsh -File .\Tests\Test-Api.ps1`. The script tests through SSH and Docker Compose, verifies requests without a password receive `401`, validates the lightweight player list, then requests and validates details for each player. It sends the password over stdin and does not print it.

## Postman

Import [PlayerDetailSync.postman_collection.json](Postman/PlayerDetailSync.postman_collection.json). Set the collection's `apiPassword` variable locally. For requests from the server machine, keep `baseUrl` as `http://127.0.0.1:8785`; for remote requests, set it to the server's reachable address after configuring the bind address and publishing the Compose port above. Run the unauthenticated health request first (`401`), then `Players (summary)`; it stores the first ID in `playerId`. Run `Player details` afterward for stats, gear, foods, and statuses.

## Data availability

`GET /api/v1/players` provides the connected-player list; `GET /api/v1/players/{playerId}` provides that player's current statuses, food timers, health/stamina/eitr, and equipped gear. Status, food, and gear icon keys can be resolved to local sprites through the public client API. Historical food consumption, activity labels, and kill/death events are not exposed yet.
