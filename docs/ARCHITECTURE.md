# Architecture

## Why this shape

Photon **Quantum 3** runs the same deterministic simulation on every client and only exchanges *inputs*; Photon Cloud
supplies a generic server that orders inputs and keeps clocks in sync. There is no game server for us to host.
Writing a custom authoritative Quantum server needs the Plugin SDK, which is limited to Photon's Gaming Circle / Enterprise Cloud.
So the "separate backend" is a normal web service with two kinds of callers:

- **Photon Cloud** calls it for custom authentication and webhooks.
- **The game client** calls it for profile, results and leaderboard.

## Flow of one match

1. **Login.** Client sends `POST /api/auth/guest {deviceId, displayName}`; the backend upserts a `Player` and returns a JWT.
2. **Photon authentication.** The client passes the JWT as the custom auth parameter `token`. Photon calls
   `/photon/auth?secret=...&token=...`; the backend validates the JWT and answers `{"ResultCode":1,"UserId":...,"Nickname":...}`.
   The JWT never has to be trusted by Photon itself; the shared `secret` stops third parties from probing the endpoint.
3. **Matchmaking.** `MatchmakingExtensions.ConnectToRoomAsync` joins/creates a 2-player room. Photon posts `/photon/webhooks/game/create`;
   the backend records a `Match` row (`Created`).
4. **Play.** `SessionRunner.StartAsync` starts the Quantum session, each client calls `AddPlayer`. `PongSystem` waits until both slots joined,
   counts down, then simulates until one side reaches 5. No backend traffic during play.
5. **Result.** When the *verified* (non-rollbackable) frame is in the game-over state, each client calls `POST /api/matches/result`.
6. **Rating.** `MatchService` stores each player's report. When two different players reported the same legal score, the match becomes
   `Finished`, Elo (K=32) is applied and wins/losses are updated in one transaction. Different scores → `Disputed`, nobody is rated.
7. **Close.** Photon posts `/photon/webhooks/game/close`; a match still `Created` becomes `Closed` (abandoned).

## Trust model

| Input | Trust | Why |
|---|---|---|
| Photon → backend calls | shared secret (`X-SecretKey`, `?secret=`), constant-time compare | an unset secret never matches |
| Client → backend calls | JWT (HS256, 24 h) | player id comes from the token, never from the body |
| Match result | two matching reports from two different players | one cheater cannot rate themselves; colluding pairs can |
| Score values | `max == WinScore`, `min < WinScore`, slot ∈ {0,1} | rejects impossible results |
| Photon `GameResult` webhook | stored raw, not used for rating | payload format depends on the game; kept for audits |

## Data model

- `Players` — id, device id (unique), display name, rating, wins, losses.
- `Matches` — Photon game id (unique), region, status (`Created|Finished|Disputed|Closed`), final score, timestamps, raw Photon result.
- `MatchPlayers` — (match, player) key, slot (unique per match), reported scores, winner flag, rating delta.

## Simulation (`game/Assets/QuantumUser/Simulation/Pong`)

State lives in Quantum *globals* (two paddle heights, ball position/velocity, scores, state machine). Using entities would not add
anything for three moving objects and would add rollback-state and asset setup. The rules use only `FP` math and integers, no randomness:
the serve angle is derived from the current score. The constant `PongSystem.WinScore` must equal `Game:WinScore` in the backend.

## Scaling and next steps

- Result reports are serialized with in-process locks; for several API replicas replace them with `pg_advisory_xact_lock(hashtext(gameId))`.
- Replace guest login with an identity provider (Google Play Games, Apple, email) and link it to `Player`.
- Use Photon's random matchmaking (`JoinRandomRoomArgs`) instead of fixed `quick-N` room names.
- Handle disconnects: forfeit after a timeout using the `/game/leave` webhook.
- Store replays (`/replay/chunk`) and re-simulate them on the backend with the same Quantum simulation DLL (`QuantumDotnetBuildSettings`) to verify results without trusting any client.
