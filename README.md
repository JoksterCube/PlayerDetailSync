# PlayerDetailSync

PlayerDetailSync exposes live player data over an authenticated server HTTP API. Connected-player identity and position are server-observed; status effects, food timers, and mount details require the client companion on each reporting player's game.

## Current API

All routes require the `X-API-Password` header and use `GET`.

| Endpoint | Description |
|---|---|
| `/api/v1/health` | Listener status, snapshot time, and connected/tracked player counts. |
| `/api/v1/players` | Lightweight list of all cached players, including offline players. |
| `/api/v1/players/{playerId}` | Player identity, position, online state, and character-data freshness. |
| `/api/v1/players/{playerId}/character` | Status effects, food timers, equipped gear, and sample age. |
| `/api/v1/players/{playerId}/stats` | Current vitals and selected native lifetime statistics. |
| `/api/v1/players/{playerId}/mount` | Retained mount record and current riding state. |
| `/api/v1/players/{playerId}/ship` | Retained ship record and current aboard state. |
| `/api/v1/players/{playerId}/keys` | Valheim's per-player unique progression keys. |
| `/api/v1/players/{playerId}/activity` | Primary activity state, active states, and observation time. |
| `/api/v1/players/{playerId}/followers` | Last-known active followers with details and sample age. |

The health response includes:

```json
{
  "status": "ok",
  "updatedAtUtc": "2026-10-06T12:00:00.0000000Z",
  "connectedPlayerCount": 2
}
```

Both list and detail endpoints read memory only: they never trigger game capture or RPCs. Example list response:

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

The player response keeps category data separate; use the category routes in the table for details. Example:

```json
{
  "playerId": "1829075840",
  "steamUserId": "76561198000000000",
  "name": "Player",
  "online": true,
  "lastSeenUtc": "2026-10-06T12:00:00.0000000Z",
  "characterDataAvailable": true,
  "characterDataAgeSeconds": 0.4,
  "position": { "x": 12.5, "y": 4.0, "z": -30.25 }
}
```

### Character data

The stats response includes current vitals and selected native lifetime statistics as `{ "name", "value", "unit" }` entries, including deaths, enemy/player/boss kills, built pieces, fishing totals, and walking/running/sailing distances. Valheim has no native lifetime distance-ridden counter, so it is not reported.

Category responses include `characterDataAgeSeconds` to show the age of the sample, but omit the repeated `playerId`, `online`, `lastSeenUtc`, and `characterDataAvailable` fields. Get player identity and connection metadata from the player detail or list endpoint.

### Mounts

The mount response returns one retained mount record and `riding`. The record remains after dismount; `riding` becomes `false`. Before a player has mounted anything, `mount` is `null`. Mount values are last observed, not simulated. The record includes active status effects with remaining timers; shield effects include remaining and maximum absorption. Mount IDs may change or be reused when the world reloads. There are no separate current/last or detail routes.

### Ships

The ship response returns one retained ship record and `aboard`. The record remains after the player leaves; `aboard` becomes `false`. Before a player has been aboard a ship, `ship` is `null`. The record includes position, speed, sail state, and whether the player was at the helm. Ship IDs are network IDs and may change on world reload. There are no separate current/last or detail routes.

### Keys and activity

The keys response contains `privateKeys`, Valheim's per-player unique keys used for individual progression. These are game keys, not cryptographic credentials. The activity response contains the primary `state`, all detected `activeStates`, and observation time. The `activity` field is omitted while collection is disabled or the player is offline. `Activity Status Collection` can be toggled on the server; character status effects are separate and remain available when activity labels are disabled. These category endpoints require the client companion for that player.

`Activity Status Collection` and `Activity Confirmation Period` are in the server-synchronized `2 - Activity` config group. The confirmation period defaults to 15 seconds (range 0-60). Routine states must remain active across reports for this duration; the routine state active longest becomes primary. Critical hazards, combat, boss targeting, raid events, and PvP bypass the delay. Short interaction events such as cooking or crafting are retained briefly so the period can distinguish sustained work from a one-off interaction. Contextual idle states wait at least 90 seconds without movement or another detected activity.

#### Activity rules and priority

The trigger column records the proposed rule; the last column describes what this version actually detects. Rows marked not detected are proposals, not emitted activity states. The number is the detector selector: lower values win immediate-state and equal-duration ties. Otherwise, the routine state active longest becomes primary. `activeStates` includes all matching urgent and confirmed states. Raw `/character` status effects are captured separately from these labels.

| Selector | Activity | Proposed trigger and validation rule | Detection in this version |
|---|---|---|---|
| 10 | Dead | `player.IsDead()` or tombstone instantiation. | `Player.IsDead()`; immediate. |
| 20 | Recovering Corpse | `CorpseRun` effect or movement toward own tombstone within 100 m. | Effect name/type contains `CorpseRun`; immediate. Tombstone direction is not tracked. |
| 30 | Drowning | Swimming, stamina at or below 5%, and taking damage. | Damage while swimming at or below 5% max stamina; immediate. |
| 40 | Freezing | `Freezing` effect without frost resistance. | Effect name/type contains `Freezing`; immediate. Resistance is not checked. |
| 50 | Burning | `Burning` status effect. | Effect name/type contains `Burning`; immediate. |
| 60 | Fighting Boss | Boss within 50 m targets player or player interacts with boss altar. | Living boss within 50 m targets the player, or successful `OfferingBowl` interaction; immediate. |
| Not assigned | Sieging Fortress | In Ashlands within 60 m of Charred Fortress with hostile spawns. | Not detected; no selector or detector is registered. |
| 70 | Hunting Serpent | On a boat/Ocean, damaging a Serpent or being targeted by one. | Player damages a character whose prefab name contains `serpent`; immediate. Being targeted alone is not detected. |
| 80 | Defending Base | Random event active while player is inside event area. | Active random event contains the player; immediate. |
| 90 | In PvP | Recent incoming/outgoing damage between players with PvP enabled. | Recent damage event between players with PvP enabled; immediate. |
| 100 | In Combat | Attack, damage, or nearby hostile alerted to player. | Attacking, blocking, targeted, or recent damage dealt/taken; immediate. |
| 110 | Dungeon Delving | Interior instance or world height above 3000. | Player height above 3000; confirmation-timed. Interior instances are not separately detected. |
| 115 | Lost at Sea | Ocean biome and no land collider within 250 m. | Ocean biome and all eight 250 m radial biome samples are Ocean; confirmation-timed. |
| 120 | Sailing | Controlling a ship or attached to boat seat/mast. | Player controls a ship or `Player.IsAttachedToShip()`; confirmation-timed. |
| 140 | Portaling | Teleport in progress or inside `TeleportWorld` trigger. | `Player.IsTeleporting()`; immediate. |
| 150 | Cart Hauling | Player attached to a `Vagon`. | Player attach point belongs to a `Vagon`; confirmation-timed. |
| 160 | Building | Hammer equipped and piece recently placed/repaired. | Build tool selected with valid placement; confirmation-timed. Placement/repair history is not tracked. |
| 171 | Terraforming | Hoe used for recent terrain flatten/raise. | Hoe-use event; recent evidence is confirmation-timed. Terrain changes are not verified. |
| 172 | Mining | Pickaxe strikes rock/ore. | Pickaxe-use event; recent evidence is confirmation-timed. Target hits are not verified. |
| 173 | Logging | Axe strikes tree/wood target. | Axe-use event; recent evidence is confirmation-timed. Target hits are not verified. |
| 174 | Farming | Cultivator used to plant, harvest, or cultivate soil. | Cultivator-use event; recent evidence is confirmation-timed. Result is not verified. |
| 175 | Taming / Ranching | Tamed-creature interaction/petting or feeding livestock. | Successful `Tameable` interaction; recent evidence is confirmation-timed. Feeding through item use is not tracked. |
| 166 | Cooking | Interact with cooking station, rack, cauldron, or oven. | `CookingStation` or successful player interaction with a cauldron/baking-oven target; recent evidence is confirmation-timed. |
| 167 | Brewing | Add wort or tap mead at fermenter. | Successful `Fermenter` interaction; recent evidence is confirmation-timed. |
| 168 | Smelting | Add ore/coal to smelter, blast furnace, or kiln. | `Smelter` input or player interaction with blast-furnace, charcoal-kiln, or Eitr-refinery target; recent evidence is confirmation-timed. |
| 165 | Crafting | Workbench/forge recipe or inventory customization. | Nearby crafting station or completed craft; confirmation-timed. Customization is not detected. |
| 169 | Sorting Chests | Open 2+ distinct containers within 60 seconds. | Two distinct successful container interactions within 60 seconds; recent evidence is confirmation-timed. |
| 170 | Fishing | Rod cast with active `FishingFloat` attached. | An owned `FishingFloat` exists; confirmation-timed. Rod/line state is not checked. |
| 176 | Trading | Store UI open with Haldor, Hildir, or Bog Witch. | Store UI visible; confirmation-timed. Trader identity is not checked. |
| 210 | Stalking / Sneaking | Crouching with stealth indicator in hostile territory. | Player crouching; confirmation-timed. Stealth/territory is not checked. |
| 177 | Hunting | Attack passive/fleeing fauna. | Player damages Deer, Hare, or Bird prefab; recent evidence is confirmation-timed. |
| 220 | Encumbered | Inventory weight exceeds carry capacity. | `Player.IsEncumbered()`; confirmation-timed. |
| 230 | Sleeping | Player sleeping in a bed. | `Player.IsSleeping()`; confirmation-timed. |
| 240 | Resting | Active `Resting` state, such as sitting or resting by a fire. | Effect name/type contains `Resting`; confirmation-timed. The lingering `Rested` buff is not used. |
| Fallback | Idling at Base | Idle 90 seconds at base (3+ pieces, comfort 5+). | Idle 90 seconds with base value at least 3 and comfort at least 5. |
| Fallback | Idling at Outpost | Idle 90 seconds near shelter/workbench with comfort below 5. | Idle 90 seconds with positive base value but below base/comfort threshold. |
| Fallback | Idling in Danger | Idle 90 seconds outside base zones. | Idle 90 seconds with no nearby base value. |
| 290 | Exploring | Reveal previously unexplored minimap pixels. | A minimap tile becomes newly explored; recent evidence is confirmation-timed. |
| Fallback | Adventuring | Overland travel outside base zones when no higher state matches. | Movement over 0.5 m between reports with no higher state. |

`Riding` (250), `Swimming` (260), `Running` (270), and `Sitting` (280) are also reported as confirmation-timed states. `Idling` is the generic fallback until one of the contextual idle labels applies. The legacy `Combat` wire value remains readable; new reports use `InCombat` (100).

The exact activity wire values this version can emit are: `dead`, `recoveringCorpse`, `drowning`, `freezing`, `burning`, `fightingBoss`, `huntingSerpent`, `defendingBase`, `inPvP`, `inCombat`, `dungeonDelving`, `sailing`, `lostAtSea`, `portaling`, `cartHauling`, `building`, `crafting`, `cooking`, `brewing`, `smelting`, `sortingChests`, `fishing`, `terraforming`, `mining`, `logging`, `farming`, `tamingRanching`, `trading`, `hunting`, `stalkingSneaking`, `encumbered`, `sleeping`, `resting`, `riding`, `swimming`, `running`, `sitting`, `exploring`, `idling`, `idlingAtBase`, `idlingAtOutpost`, `idlingInDanger`, and `adventuring`. `siegingFortress` is accepted as an enum value but is not emitted because no detector exists yet. `combat` is accepted only for backward compatibility; new reports emit `inCombat`.

### Followers

The followers response returns every last-known active follower with full details and `characterDataAgeSeconds`. Each follower includes its ID, display name, species, position, level, tame/hunger/alert state, health, and status effects. There is no separate per-follower detail route. The lightweight player detail response does not embed follower data.

`name` is the game's display name, including a pet's custom name such as Thora or Rex; `species` remains the localized creature type. Custom display names are not localized again by the API.

`followerId` is Valheim's composite network-object ID (`namespace:objectCounter`), not an array index, Steam ID, or pet owner's player ID. Every client and server sees the same ID for the same network object; list reordering and ownership changes do not change it. It normally survives a player disconnect/reconnect while the world keeps running, but the current game reconstructs IDs on world load. Server/world restarts may therefore change or reuse IDs, and newly summoned/recreated creatures get new IDs. Do not use this value as a permanent pet identity or join historical data across world reloads. A durable identity would require a separate mod-assigned identifier persisted on the creature. Missing players return `404`. The followers route requires the existing `X-API-Password` header.

Only loaded, living creatures whose AI follow target is the player are included, including commanded tame pets and following summons. Nearby pets told to stay are excluded. Accepted client reports are preferred over server-side character copies because the follow target and status effects can be client-owned. Install this updated DLL on both server and clients. Modded companions with a different AI/follow mechanism or resources not exposed by Valheim's Character API are not guaranteed to appear.

Follower stats contain only `health` and `maxHealth`. Starting with version 0.5.0, `stamina`, `maxStamina`, `eitr`, and `maxEitr` are removed from followers; player stats are unchanged. `hungry` is `null` without a Tameable component. Protection bubbles derived from Valheim's shield effect include remaining and maximum damage absorption; other effects omit those fields. Status countdowns are aged and clamped between online reports, but health, shield absorption, position, and follower membership are last observed, not simulated. Zero remaining time does not assert a status is still active; wait for the next report to confirm removal. Offline records preserve last-known followers and timers, so check the metadata before treating the list as current. Reports allow up to 64 followers and 64 effects per follower, with a 262,144-character payload limit; oversized or invalid reports are rejected.

Automatic collection runs at the server-synchronized `Refresh Interval` (default 5 seconds, configurable from 1-60 seconds). Existing configs retain their saved interval. Joining/rejoining triggers an immediate refresh; missing-report retries back off through 1, 2, 4, 8, 16, and 30 seconds, then remain at 30 seconds until a report is accepted. Retry state resets on acceptance or disconnect, and client captures remain limited to once per second. Routine refresh/report logs are debug-only; rejected reports log the failing field at most once per 10 seconds. Players are captured only after a stable character ID is available from their server-side character or an accepted client report. Temporary network IDs are never published or cached. These IDs are character IDs, not Steam IDs.

The server saves `JoksterCube.PlayerDetailSync.players.json` beside its BepInEx config. Cache serialization and file writes run on a background worker with a single pending save; the file is loaded at startup with every restored player initially offline. Do not share the cache publicly: it contains player positions and character data. A failed save is logged; abrupt termination can lose the most recent pending update. The cache belongs to this server installation; do not reuse it for another world.

The cache and API keep exactly one record per character ID, whether online or offline. Reconnecting updates that record rather than adding another. Duplicate IDs in a saved cache are collapsed to the most recently seen record at startup. Legacy caches without the new `identityVersion` marker are cleared once because they may contain temporary network IDs; old offline history is discarded and records rebuild as players are identified. A joining player with no known character ID is omitted until identification succeeds, so the health endpoint's connected-player count covers identified players only.

`steamUserId` is the Steam account ID observed by the server on native Steam connections or Steam players using crossplay, serialized as a string in both list and detail responses and retained in the cache. Crossplay identities are parsed using Valheim's platform-ID parser; other platforms are not treated as Steam users. It is an empty string when unavailable, including non-Steam players. One Steam account can have multiple characters, and names are not unique: distinct character IDs remain separate even when their names or Steam IDs match.

Offline records retain last-known vitals, equipment, foods, and statuses rather than implying those values are current. `online` indicates connection state, `lastSeenUtc` records the most recent online sample, and `characterDataAgeSeconds` indicates detail age (`-1` if none was received). Online timed effects are adjusted between client reports; offline timed effects remain as last observed. If a report has never arrived, `characterDataAvailable` is false; polling the API cannot force data from an absent or nonreporting client.

Effects and foods include `hasDuration` and `remainingSeconds`. Untimed statuses have `hasDuration: false` and `remainingSeconds: 0`; zero does not mean they have expired. Timed statuses and foods have `hasDuration: true`, with expired countdowns clamped to zero. Statuses, foods, and gear include an `iconKey` based on the game's sprite name. The key identifies a game sprite; it is not image data. `characterDataAvailable` indicates whether character data is available, either from a server-side character object or a recent client report. If false, install/enable the mod on that player's client; their status, food, stat, and equipment data will otherwise be unavailable.

## Installation and access

Install the mod on the dedicated server (or the server host) with BepInEx. To include status effects and food timers, install and enable the same mod version on each player's client. Clients send their own active effects to the server over Valheim's authenticated routed RPC; they do not need the HTTP API password. The server config file is `BepInEx/config/JoksterCube.PlayerDetailSync.cfg`.

Server and clients must run the exact same PlayerDetailSync version. The version handshake disconnects clients that report a different version; peers that do not answer within 15 seconds are also disconnected. ServerSync's minimum version is set to the current version as a second check. `Enabled`, `Refresh Interval`, `Activity Status Collection`, and `Activity Confirmation Period` are synchronized from the server and enforced on clients. Refreshes are clamped to one per second or slower to limit gameplay overhead. `Lock Configuration` restricts changes to synchronized entries to server admins. `Bind Address`, `Port`, and `Password` are server-local and are never synchronized. ServerSync is bundled inside PlayerDetailSync, so no separate ServerSync install is required.

The API binds to `127.0.0.1:8785` by default, so it is reachable only from the server machine. Set a non-empty `Password` in the config to enable the API; with the default empty password, the listener will not start. Example request from PowerShell:

```powershell
$password = "<Password from the server config>"
Invoke-RestMethod -Headers @{ "X-API-Password" = $password } -Uri "http://127.0.0.1:8785/api/v1/players"
```

For remote access, set `Bind Address` to `0.0.0.0` and configure your host network or firewall to allow TCP port `8785` only from trusted addresses. Send the password in the `X-API-Password` header. Plain HTTP does not encrypt the password in transit; use a trusted network or an HTTPS reverse proxy. Do not expose the port to untrusted networks. On Windows, `HttpListener` may require a URL reservation for the selected prefix.

Configuration changes take effect live. Changing the bind address, port, or password restarts the listener.

## Data availability

The table above lists the available routes. Player identity and connection state are separate from category data such as statuses, food timers, vitals, and equipped gear. Status, food, and gear icon keys can be resolved to local sprites through the public client API. Historical food consumption and kill/death events are not exposed yet.
