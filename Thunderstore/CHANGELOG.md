| Version | Update Notes |
|----------|---------------|
| **1.0.0** | Breaking follower schema change: removed stamina, maxStamina, eitr, and maxEitr from follower stats. Followers expose health and maxHealth only; player stats are unchanged. |
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