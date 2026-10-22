# Photon Pong

A minimal real-time multiplayer game built on **Photon Quantum 3** (deterministic, rollback netcode) with a **separate, self-hosted backend** (ASP.NET Core + PostgreSQL, run with Docker on Linux). The Unity client is built for **Android by GitHub Actions**.

```
Unity client (Quantum 3 simulation)
   │  1. POST /api/auth/guest ───────────────────────────────┐
   │  2. connect to Photon Cloud (custom auth token) ─┐      │
   │                                                  ▼      ▼
   │                                     Photon Cloud ──► Backend (this repo, /backend)
   │                                      (relays inputs)   • /photon/auth      custom authentication
   │  3. play: inputs only, both clients simulate  │        • /photon/webhooks  room lifecycle
   │  4. POST /api/matches/result ───────────────────────►  • /api/*            profile, ratings, leaderboard
   └──────────────────────────────────────────────────────  PostgreSQL
```

Quantum has no game server you write code for (that needs a paid Photon Enterprise plan), so the backend is
an ordinary web service that Photon Cloud calls (authentication, webhooks) and the game calls (profile, results).
Ratings are only updated when **both** players report the same score. See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Repository layout

| Path | What |
|---|---|
| `backend/` | ASP.NET Core 8 API, EF Core migrations, xUnit tests, `Dockerfile` |
| `docker-compose.yml` | PostgreSQL + API (+ optional Caddy for automatic HTTPS) |
| `game/` | Unity 2022.3 project: Pong simulation (`QuantumUser/Simulation/Pong`), client (`QuantumUser/View/Pong`), CI build script (`Editor/`) |
| `.github/workflows/` | `backend.yml` (build, test, docker build), `game-simulation.yml` (DSL + simulation check against the SDK, no Unity needed), `unity-android.yml` (APK build), `release.yml` (GitHub Release with APK, container, sample .env) |

## 1. Server preparation (Linux + Docker + PostgreSQL)

**You need**

- A Linux server (Ubuntu 22.04/24.04 or Debian 12), 1 vCPU / 1 GB RAM is enough for development.
- A **domain name** pointing at the server and **ports 80 and 443 open**. Photon Cloud must reach your backend from the internet over **HTTPS with a valid certificate** (self-signed certificates are rejected).
- Docker Engine 24+ with the Compose v2 plugin.
- A Photon account with a **Quantum 3** application (App ID) — <https://dashboard.photonengine.com>.

**Install Docker** (Ubuntu/Debian):

```bash
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker "$USER"   # log out and in again
docker compose version
```

**Firewall** (ufw example). PostgreSQL is *not* published to the host, so do not open 5432:

```bash
sudo ufw allow OpenSSH && sudo ufw allow 80,443/tcp && sudo ufw enable
```

### PostgreSQL

The default setup runs PostgreSQL 16 in the compose stack with a named volume (`pgdata`). Nothing to install:
the database, user and password come from `.env` and the API creates/updates the schema on startup (EF Core migrations).

Using an **existing PostgreSQL server** instead? Remove the `db` service from `docker-compose.yml`, then:

```sql
CREATE ROLE pong LOGIN PASSWORD 'a-strong-password';
CREATE DATABASE pong OWNER pong;
```

and set the API connection string, e.g. `ConnectionStrings__Default=Host=db.internal;Port=5432;Database=pong;Username=pong;Password=...;SSL Mode=Require`.
The role needs to own the database because the API runs its own migrations.

**Backups** (run from cron):

```bash
docker compose exec -T db pg_dump -U pong pong | gzip > pong-$(date +%F).sql.gz
```

### Run the backend

```bash
git clone https://github.com/cds-id/photon-backend.git && cd photon-backend
cp .env.example .env
#   POSTGRES_PASSWORD  - any strong password
#   JWT_SECRET         - openssl rand -base64 48
#   PHOTON_WEBHOOK_SECRET, PHOTON_AUTH_SECRET - openssl rand -hex 24 (each)
#   API_DOMAIN         - e.g. api.example.com
docker compose --profile tls up -d --build    # with automatic HTTPS (Caddy)
# or: docker compose up -d --build            # API on 127.0.0.1:8080, bring your own reverse proxy

curl https://api.example.com/health           # -> Healthy
docker compose logs -f api
```

Update later with `git pull && docker compose --profile tls up -d --build`.

## 2. Photon dashboard configuration

In the dashboard open your **Quantum 3** application → **Manage**.

1. **Custom authentication** → add a provider of type *Custom* with URL
   `https://api.example.com/photon/auth?secret=<PHOTON_AUTH_SECRET>`.
   The client sends the player's JWT as the `token` parameter; the backend returns the player id as the Photon `UserId`.
2. **Plugins → Edit → Add New Pair** (values are max 1024 characters, changes apply within a minute):

   | Key | Value |
   |---|---|
   | `WebHookBaseUrl` | `https://api.example.com/photon/webhooks` |
   | `WebHookSecret` | same value as `PHOTON_WEBHOOK_SECRET` |
   | `WebHookEnableOnCreate` | `true` |
   | `WebHookEnableOnClose` | `true` |
   | `WebHookEnableGameResult` | `true` (optional, stored for auditing) |

3. Copy the **App ID** into the Unity project (Quantum Hub / `PhotonServerSettings`, see below).

## 3. Unity client

The Quantum SDK (**3.0.14 stable, build 2241**) is committed in `game/Assets/Photon/` so CI needs no extra download. It is proprietary software under [Photon's terms](https://www.photonengine.com/terms): **keep this repository private**. One-time local setup:

1. Install **Unity 2022.3 LTS** (project is pinned to `2022.3.62f1`) with the *Android Build Support* module.
2. Open `game/` in Unity (the SDK is already in `Assets/Photon`; to upgrade it, import a newer `.unitypackage` over it). Let the **Quantum Hub** finish its setup (creates `Assets/QuantumUser/...` configs; keep them in version control) and enter your Quantum 3 **App ID**.
3. The DSL file `Simulation/Pong/Pong.qtn` is compiled by Quantum's code generation on import. Run it from the Quantum menu if it does not run by itself.
4. Open the **SystemsConfig** asset (Quantum Hub / `Assets/QuantumUser/Resources`) and add `PongSystem` to the system list.
5. Create a default **Map** asset (the Quantum Hub does this; Pong does not use the map's contents).
6. Menu **Pong → Create Main Scene**, select the `Pong` object and assign the **Map** under `PongGame → Runtime Config`. Set the **Backend URL** (default `http://10.0.2.2:8080` is the host machine from the Android emulator).
7. Commit `Assets/Scenes/`, the Quantum config assets **and every `.meta` file** (scene references use the `.meta` GUIDs).

Test two clients locally: one editor + one standalone build (or two builds), tap **Quick match** on both.
Controls: `W/S` or arrow keys; on touch/mouse the paddle follows your finger/pointer height.

### Quantum SDK in CI

`game/Assets/Photon` is part of the repository, so the Unity and simulation workflows work out of the box. If you ever move the SDK out of
git (for example to make the repository public), zip it with the path `Assets/Photon/...` inside, host it privately and set the
`QUANTUM_SDK_URL` secret (optionally `QUANTUM_SDK_TOKEN`); the workflows download it when `Assets/Photon` is missing.

## 4. GitHub Actions

**`backend.yml`** runs on every backend change: restore, build, test, `docker build`, compose validation. No secrets needed.

**`unity-android.yml`** builds `PhotonPong.apk` and uploads it as an artifact (*Actions → run → Artifacts*). It needs these
**repository secrets** (*Settings → Secrets and variables → Actions*); without them the job logs a warning and skips the build:

| Secret | Purpose |
|---|---|
| `UNITY_LICENSE` | Contents of the `Unity_lic.ulf` file created by Unity Hub (see below). |
| `UNITY_EMAIL`, `UNITY_PASSWORD` | Unity account used for activation (Personal licenses). |
| `UNITY_SERIAL` | Alternative to `UNITY_LICENSE` for Pro/Plus serial activation. |
| `QUANTUM_SDK_URL`, `QUANTUM_SDK_TOKEN` | *(optional)* only needed if `game/Assets/Photon` is not committed, see above. |

**Getting the license file (Unity Personal / free).** `game-ci/unity-request-activation-file` is discontinued; the current method is
([game-ci docs](https://game.ci/docs/github/activation)):

1. Install Unity Hub on any computer and sign in with the Unity account CI should use (a dedicated CI account is safest).
2. *Preferences → Licenses → Add → free personal license*, and wait until activation finishes.
3. Copy the file `Unity_lic.ulf`: Windows `C:\ProgramData\Unity\Unity_lic.ulf`, macOS `/Library/Application Support/Unity/Unity_lic.ulf`, Linux `~/.local/share/unity3d/Unity/Unity_lic.ulf` (folders may be hidden; a license can show in the Hub without the file existing, then re-activate).
4. Paste the whole file content into the `UNITY_LICENSE` secret, and set `UNITY_EMAIL` / `UNITY_PASSWORD` (2FA is not supported by headless activation).

Unity has been removing offline activation for Personal seats, so this can stop working; fallbacks are a Pro/Plus `UNITY_SERIAL`, a self-hosted runner with Unity Hub logged in, or building the APK locally.

Optional repository **variable** `PONG_ALLOW_HTTP=1` lets the APK talk to a plain-`http://` backend (development only).
The APK is signed with Unity's debug key, which is fine for sideloading; add a keystore before publishing to Google Play.

### Releases (APK + container + sample .env)

Pushing a tag publishes a GitHub Release (*Releases* page) through `release.yml`:

```bash
git tag v0.1.0 && git push origin v0.1.0      # or: Actions > Release > Run workflow
```

| Release asset | What |
|---|---|
| `pong-api-<tag>.tar.gz` | the API container image (`gunzip -c ... \| docker load`); also pushed to `ghcr.io/<owner>/<repo>-api:<tag>` and `:latest` |
| `pong-backend-deploy-<tag>.zip` | production `docker-compose.yml` (uses the prebuilt image, no source needed), `Caddyfile`, `.env.example` |
| `env.example` | the sample `.env` as a standalone file (GitHub drops leading dots from asset names) |
| `PhotonPong.apk` | Android build, **only when the Unity secrets above are set**; otherwise the release notes say it is missing |

New GHCR packages are private by default: make the package public in the repository's *Packages* settings, or `docker login ghcr.io` with a token that has `read:packages`.
On the server: unzip the bundle, `cp .env.example .env`, fill in the secrets, `docker compose --profile tls up -d`.

## 5. Backend development without Docker

```bash
cd backend
dotnet test                                   # 16 tests, in-memory database
# run against a local PostgreSQL (see the SQL above); Development settings are in appsettings.Development.json
dotnet run --project src/PhotonBackend.Api
```

Add a migration after changing entities: `dotnet ef migrations add <Name> -p src/PhotonBackend.Api -o Data/Migrations`.

### HTTP API

| Method & path | Auth | Purpose |
|---|---|---|
| `POST /api/auth/guest` | – | `{deviceId, displayName}` → player + JWT |
| `GET /api/me` | JWT | profile, rating, record |
| `GET /api/leaderboard?limit=` | – | top players by rating |
| `GET /api/matches` | JWT | last 20 finished matches |
| `POST /api/matches/result` | JWT | `{gameId, slot, scoreLeft, scoreRight}`; rated once both players agree |
| `GET/POST /photon/auth` | `?secret=` | Photon custom authentication |
| `POST /photon/webhooks/**` | `X-SecretKey` | Photon room lifecycle webhooks |
| `GET /health` | – | liveness + database check |

## 6. Status and known gaps

Read this before trusting the Unity side.

- **Backend: verified.** The 16 tests pass, and the API was run against a real PostgreSQL 16 (migration, login, Photon auth response, webhook, leaderboard). The Docker image could not be built in the authoring environment (no Docker daemon); the same Dockerfile is built by `backend.yml` in CI.
- **Simulation: verified against the real SDK (3.0.14).** `Pong.qtn` compiles with the SDK's own DSL compiler, `PongSystem.cs` compiles against the SDK's simulation sources and the generated code, and the paddle-bounce behaviour is checked with Quantum's `FP` math (`game/tools/SimulationCheck/run.sh`, also run by `game-simulation.yml` on every simulation change). Not covered: the full system update loop and `OnPlayerAdded` wiring, which need a running session.
- **Unity client code: checked by reading the SDK sources, not compiled.** Every Quantum/Photon API used in `PongGame`/`PongInput`/`PongView` (`MatchmakingExtensions.ConnectToRoomAsync`, `MatchmakingArguments.AuthValues`, `SessionRunner.StartAsync`, `QuantumGame.AddPlayer`/`PlayerIsLocal`, `CallbackPollInput.SetInput`, `CallbackPluginDisconnect`, `RealtimeClient.DisconnectAsync`) was matched against the SDK's source or assemblies, but there was no Unity license to compile it. Expect at most small fixes on the first import.
- **Unity project setup is manual once**: Quantum Hub creates the config assets (`SystemsConfig`, Map, App ID in `PhotonServerSettings`); CI cannot create them.
- Quick match uses Photon's built-in random matchmaking (`RoomName = null`); private rooms use a 4-digit code.
- No reconnect/forfeit handling: if a player quits mid-match the other must tap **Leave**; no rating is changed.
- `MatchService` serializes result reports with in-process locks, so run **one** API instance (or add a database advisory lock before scaling out).
- Guest accounts are keyed by a device id stored in `PlayerPrefs`; add a real identity provider before public launch.
- Everything is rated on player-reported results cross-checked between two players. Colluding players can fabricate a result; Photon's server-side result webhook (Enterprise plugin SDK) is the way to close that.
