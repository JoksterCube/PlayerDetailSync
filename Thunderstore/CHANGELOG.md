| Version | Update Notes |
|----------|---------------|
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