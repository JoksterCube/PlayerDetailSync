| Version | Update Notes |
|----------|---------------|
| **0.14.1** | Fix `player_login` reporting position `0, 0, 0`: login is sent once the character spawns (or after 60 seconds) with its spawn position. Webhook player positions come from the character's synced position, and unknown positions are omitted instead of reported as the world origin. |
| **0.14.0** | Breaking webhook changes: removed `player_spawned` and `first_boss_key_acquired` (covered by `first_boss_kill`) and their settings. `pvp_kill` now reports the victim as the player with `killerId`/`killerName`. `player_death` reports `cause` (creature, player, fall, drowning, tree, and other hit types, or natural causes) and the killer. Every event includes a `position` (boss/miniboss death spot, victim, player, or raid center). First boss and miniboss kills persist across restarts. New Discord tags: `{killer id}`, `{cause}`, `{position}`. Update both server and clients. |
| **0.13.5** | Fix repeat boss and miniboss kills within a minute of the previous kill not sending `boss_kill`/`miniboss_kill`; duplicate key sources for one kill are now merged within 5 seconds. Update both server and clients. |
| **0.13.4** | Added a `first_boss_kill` webhook, sent alongside `boss_kill` when the player did not hold that boss key before the kill; configurable with `First Boss Kill` and `First Boss Kill Template`. Documented detected bosses and minibosses. Update both server and clients. |
| **0.13.3** | Miniboss kill webhooks cover every creature with a boss health bar that is not a campaign boss, plus Hildir's minibosses; earlier phases of campaign bosses are excluded. Detected minibosses are logged on server start. Update both server and clients. |
| **0.13.2** | Only campaign bosses trigger boss kill webhooks; ordinary creatures that set defeat keys (bats, trolls, surtlings, and similar) are ignored and logged separately on server start. Update both server and clients. |
| **0.13.1** | Fix player death and PvP kill webhooks on dedicated servers. Boss kill credit depends on how keys are granted: World Advancement Progression private-key recipients when it is installed, otherwise every online player for a world key, or only the key holder with player-based events. Detected boss defeat keys are logged on server start. Update both server and clients. |
| **0.13.0** | Fix boss and miniboss kill webhooks on dedicated servers. Boss kills credit every player who receives the boss key, including World Advancement Progression's range-limited private keys; any boss that sets a defeat key is detected, including the Deep North boss. Hildir miniboss kills credit every player who damaged them. Update both server and clients. |
| **0.12.0** | Retain the last classified activity and its observation time across state changes, disconnects, and cache reloads. Offline players report `offline` with their last gameplay activity; repeated reports and transient states do not overwrite classified history. Update both server and clients. |
| **0.11.0** | Breaking API change: move vitals from `/stats` to `/character`. Add global mount, ship, and follower list/detail routes with character relationship IDs; reconcile unique riders/follow targets and retained ship passengers/last drivers. Report pending activity as `unclassified`; add Steam account display names after character names. Update both server and clients. |
| **0.10.0** | Report all positive Valheim lifetime statistics by their native stat names; omit zero-valued stats and units. |
| **0.9.9** | Use Valheim's saved follow target when capturing pets so ownership changes update follower lists. |
| **0.9.8** | Added a temporary `joining` activity state on player spawn. |
| **0.9.7** | Return null activity explicitly when no activity state is available; clarify that unique keys may include mod-defined values. |
| **0.9.6** | Ignore blank private keys in client reports so they do not discard all reported player data. |
| **0.9.5** | Marked all configuration settings as server-only and hide them from clients. |
| **0.9.4** | Reset client reporting on every join/rejoin and retry once per second until the server acknowledges an accepted report. Resolve character IDs from server-held network data when loaded characters and client reports are unavailable. Update both server and clients. |
| **0.9.3** | Reduced repeated character scans during capture, coalesced client-report snapshot updates, and removed a serialization buffer copy. |
| **0.9.2** | Made webhook event and template tag handling enum-based while preserving existing JSON event names. |
| **0.9.1** | Added per-event Discord message templates for localized webhook messages, shown only when Discord format is selected. |
| **0.9.0** | Added Discord-compatible and structured JSON webhooks with configurable player, boss, miniboss, PvP, and raid event notifications. |
| **0.8.1** | Prioritized confirmed fortress sieging and dungeon delving over combat. |
| **0.8.0** | Replaced generic idling with contextual idle states and added confirmation-timed fortress siege detection. |
| **0.7.0** | Removed obsolete activity wire values, delayed generic idling until confirmation, and added gathering detection for pickable resources. |
| **0.6.2** | Removed redundant project metadata to keep assembly version and product information from drifting. |
| **0.6.1** | Refined activity selector priorities; corrected resting, coastal sea, passenger sailing, station coverage, and boss altar detection. |
| **0.6.0** | Added synchronized activity collection controls, confirmation timing, prioritized activity labels, and event-backed work/combat detection. |
| **0.5.7** | Added mount status effects, including shield remaining and maximum absorption. |
| **0.5.6** | Refactored internal API organization and typed activity/statistic metadata while preserving serialized JSON values. |
| **0.5.5** | Removed repeated player identity, connectivity, and availability fields from category responses; retained category data age. |
| **0.5.4** | Replaced current/last mount and ship routes with one `/mount` and `/ship` record per player plus riding/aboard state. Retained the last observed record after dismount or leaving. |
| **0.5.3** | Follower lists now include complete follower records; removed the redundant per-follower detail route. |
| **0.5.2** | Reorganized player details into category endpoints for character data, mounts, ships, lifetime stats, private keys, and activity. Added current/last ship tracking, native player statistics, per-player unique keys, and extensible activity detectors; compacted the player detail response. |
| **0.5.1** | Added current and last mount details to player reports and API responses, including mount identity, species, level, health, stamina, and tame state. Last mounts persist through dismounts, disconnects, and cache reloads. |
| **0.5.0** | Breaking follower schema change: removed stamina, maxStamina, eitr, and maxEitr from follower stats. Followers expose health and maxHealth only; player stats are unchanged. |
| **0.4.1** | Fixed follower names to use the game's custom display name (for example, Thora or Rex) rather than the species name. Clarified that follower network IDs are shared across clients but may be reassigned when the world reloads. |
| **0.4.0** | Added active pet/summon follower summaries and stable-ID follower detail endpoints with positions, names, levels, health, supported stamina/eitr, hunger/alert state, status timers, and protection-bubble absorption. Followers are client-reported and retained in the persistent cache with online/offline freshness metadata. |
| **0.3.2** | Added server-observed steamUserId to player summaries, details, and persistent cache, including Steam players connected through crossplay. Enforced one entry per stable character ID, stopped caching temporary network IDs, and added one-time legacy cache cleanup and startup deduplication. |
| **0.3.1** | Fixed untimed/expired status report rejection and modded gear quality validation; added hasDuration, field-specific rejection diagnostics, and backed-off refresh retries with quiet routine logs. |
| **0.3.0** | Automatic persistent player cache, cache-only API reads, online/offline and last-seen metadata, immediate join/missing-data refresh, and stable client character IDs. |
| **0.2.12** | Added rate-limited diagnostics for targeted character-data refresh. |
| **0.2.11** | Split lightweight player list from on-demand per-player details endpoint. |
| **0.2.10** | Added diagnostics for on-demand client report delivery. |
| **0.2.9** | Capped client reports and server snapshots at one update per second to reduce gameplay overhead. |
| **0.2.8** | Resolve client report senders through Valheim's routed RPC peer map. |
| **0.2.7** | Keep client character reports available for the connection lifetime and expose report age. |
| **0.2.6** | Request-triggered client refresh for missing player character data. |
| **0.2.5** | Added player vitals, equipped gear, and public icon-key lookup API. |
| **0.2.4** | Localized status and food names in player snapshots. |
| **0.2.3** | Fixed server client-report lookup to use the public Valheim peer list. |
| **0.2.2** | Fixed status/food report matching when Valheim does not populate the peer player ID. |
| **0.2.1** | Fixed client routed-RPC targeting for status and food reports. |
| **0.2.0** | Added ServerSync version enforcement, server-controlled API settings, and client reporting for player statuses and food timers. |
| **0.1.0** | Initial server-hosted HTTP API scaffold. Exposes connected player IDs, names, and positions. |