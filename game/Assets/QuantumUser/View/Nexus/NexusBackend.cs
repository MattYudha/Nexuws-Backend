using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Nexus
{
    /// <summary>
    /// Klien REST ke PhotonBackend.Api (meta: guest auth, matchmaking MMR, lapor hasil).
    /// Dipakai flow Solo 1v1 Ranked. Async pakai HttpClient (OK di Editor + Standalone).
    /// JSON via JsonUtility (DTO sederhana, nama field = JSON camelCase dari API).
    /// </summary>
    public sealed class NexusBackend
    {
        private readonly HttpClient _http;

        public string Token { get; private set; } = "";
        public string PlayerId { get; private set; } = "";
        public string DisplayName { get; private set; } = "";
        public string Username { get; private set; } = "";
        public string RankTier { get; private set; } = "";
        public int Rating { get; private set; }
        public int Faction { get; private set; }     // 0 none, 1 ORDE, 2 LEGION
        public int Wins { get; private set; }
        public int Losses { get; private set; }
        public int MatchesPlayed { get; private set; }
        public float WinRate { get; private set; }
        public int Kills { get; private set; }
        public int Deaths { get; private set; }
        public int Assists { get; private set; }
        public float Kda { get; private set; }
        public int AccountLevel { get; private set; }
        public int AccountXp { get; private set; }
        public string AccountRank { get; private set; } = "";
        public bool LoggedIn => Token.Length > 0;

        public NexusBackend(string baseUrl)
        {
            _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(10) };
        }

        // ---------- DTO ----------
        [Serializable] private sealed class GuestReq { public string deviceId; public string displayName; }
        [Serializable] private sealed class RegisterReq { public string username; public string password; public string displayName; }
        [Serializable] private sealed class LoginReq { public string username; public string password; }
        [Serializable] private sealed class AuthRes { public string playerId; public string username; public string displayName; public int faction; public int rating; public string token; }
        [Serializable] private sealed class FactionReq { public int faction; }
        [Serializable] private sealed class LoginRes { public string playerId; public string displayName; public int rating; public string token; }
        [Serializable] private sealed class RegionReq { public string region; public string buildVersion; public string simVersion; }
        [Serializable] public sealed class Matchmaking { public string state; public string gameId; public int slot; public string region; public string opponentId; public string message; }
        // Payload hasil LENGKAP (docs/32 KEAMANAN MINIMUM): tiap klien kirim statistik SEMUA pemain
        // (deterministik → identik), backend bandingkan & tolak bila beda.
        [Serializable] public sealed class HeroStatRow { public int heroId; public int playTicks; public int kills; public int damage; }
        [Serializable] public sealed class PlayerStatRow {
            public int slot; public int kills; public int botKills; public int deaths; public int heroId;
            public int damageDealt; public int damageTaken; public int skillsUsed; public int skillsHit;
            public int hitsBlocked; public int dodges; public int guardCrushes; public int criticals; public int gearDrops;
            public bool firstBlood; public int maxKillStreak; public int maxDeficit; public int heroSwitches; // firstBlood = bool (cocok dgn backend)
            public HeroStatRow[] heroes;
        }
        [Serializable] private sealed class ResultReq {
            public string gameId; public int slot; public int scoreLeft; public int scoreRight;
            public int kills; public int deaths; public int assists; public int heroId;
            public string mode; public int durationSec; public int partySize;
            public PlayerStatRow[] players;
        }
        [Serializable] private sealed class StatusRes { public string status; public string message; }
        [Serializable] private sealed class MeRes { public string playerId; public string username; public string displayName; public int faction; public int rating; public string rankTier; public int accountLevel; public string accountRank; public int accountXp; public int wins; public int losses; public int matchesPlayed; public float winRate; public int kills; public int deaths; public int assists; public float kda; }
        [Serializable] public sealed class LeaderRow { public int rank; public string displayName; public string tier; public int ladderPoints; public int wins; public int losses; public float winRate; public int matches; }
        [Serializable] private sealed class LeaderWrap { public LeaderRow[] items; }
        [Serializable] public sealed class MyRankRes { public bool placed; public int rank; public string tier; public int ladderPoints; public int wins; public int losses; public float winRate; public int matches; public int needed; public int rankedMatches; }
        [Serializable] public sealed class MatchHeroRow { public int heroId; public int playTicks; public int kills; public int damage; }
        [Serializable] public sealed class MatchPlayerRow {
            public int slot; public string displayName; public int heroId; public int kills; public int botKills; public int deaths; public int assists;
            public int ratingDelta; public int ratingBefore; public bool isWinner; public bool hasDetailedStats;
            public int damageDealt; public int damageTaken; public int hitsBlocked; public int dodges; public int guardCrushes; public int criticals;
            public int skillsUsed; public int skillsHit; public bool firstBlood; public int maxKillStreak; public int maxDeficit; public int heroSwitches;
            public MatchHeroRow[] heroes;
        }
        [Serializable] public sealed class MatchRow { public string gameId; public string endedAt; public string mode; public string format; public int durationSec; public int slot; public int scoreLeft; public int scoreRight; public bool won; public int ratingDelta; public int ratingBefore; public int heroId; public int partySize; public MatchPlayerRow[] players; }
        [Serializable] private sealed class MatchWrap { public MatchRow[] items; }

        // ---------- Auth ----------
        /// <summary>Login tamu berbasis device id (stabil per mesin). Mengisi Token/PlayerId/Rating.</summary>
        public async Task<bool> GuestLoginAsync(string deviceId, string displayName)
        {
            var res = await PostAsync("/api/auth/guest", JsonUtility.ToJson(new GuestReq { deviceId = deviceId, displayName = displayName }), auth: false);
            if (res == null) return false;
            var dto = JsonUtility.FromJson<LoginRes>(res);
            Token = dto.token ?? "";
            PlayerId = dto.playerId ?? "";
            DisplayName = dto.displayName ?? "";
            Rating = dto.rating;
            return LoggedIn;
        }

        /// <summary>Daftar akun baru (username + password). Mengisi profil penuh.</summary>
        public async Task<bool> RegisterAsync(string username, string password, string displayName)
        {
            var res = await PostAsync("/api/auth/register", JsonUtility.ToJson(new RegisterReq { username = username, password = password, displayName = displayName }), auth: false);
            return await ConsumeAuth(res);
        }

        /// <summary>Login akun terdaftar.</summary>
        public async Task<bool> LoginAsync(string username, string password)
        {
            var res = await PostAsync("/api/auth/login", JsonUtility.ToJson(new LoginReq { username = username, password = password }), auth: false);
            return await ConsumeAuth(res);
        }

        private async Task<bool> ConsumeAuth(string res)
        {
            if (res == null) return false;
            var dto = JsonUtility.FromJson<AuthRes>(res);
            Token = dto.token ?? ""; PlayerId = dto.playerId ?? ""; Username = dto.username ?? "";
            DisplayName = dto.displayName ?? ""; Rating = dto.rating; Faction = dto.faction;
            if (LoggedIn) await RefreshMeAsync();
            return LoggedIn;
        }

        public async Task SetFactionAsync(int faction)
        {
            var res = await PostAsync("/api/me/faction", JsonUtility.ToJson(new FactionReq { faction = faction }), auth: true);
            if (res != null) Faction = faction;
        }

        // ---------- Matchmaking ----------
        // Versi simulasi — BUMP tiap kali Nexus.qtn / logika sim berubah (harus sama dgn Game:RequiredSimVersion backend).
        public const string SimVersion = "nx-sim-2026-10-29-1v1b";

        public async Task<Matchmaking> JoinQueueAsync(string region, string buildVersion)
        {
            var body = JsonUtility.ToJson(new RegionReq { region = region, buildVersion = buildVersion, simVersion = SimVersion });
            var res = await PostAsync("/api/matchmaking/join", body, auth: true);
            return res == null ? null : JsonUtility.FromJson<Matchmaking>(res);
        }

        public async Task<Matchmaking> QueueStatusAsync()
        {
            var res = await GetAsync("/api/matchmaking/status");
            return res == null ? null : JsonUtility.FromJson<Matchmaking>(res);
        }

        public async Task LeaveQueueAsync()
        {
            await PostAsync("/api/matchmaking/leave", "{}", auth: true);
        }

        // ---------- Result ----------
        /// <summary>Lapor hasil 1v1. slot = slot pemain ini (dari identitas in-game), scoreLeft/Right absolut (sim deterministik → identik di kedua klien).</summary>
        public async Task<string> ReportResultAsync(string gameId, int slot, int scoreLeft, int scoreRight, int kills, int deaths, int assists, int heroId, string mode, int durationSec, int partySize, PlayerStatRow[] players = null)
        {
            var body = JsonUtility.ToJson(new ResultReq { gameId = gameId, slot = slot, scoreLeft = scoreLeft, scoreRight = scoreRight, kills = kills, deaths = deaths, assists = assists, heroId = heroId, mode = mode, durationSec = durationSec, partySize = partySize, players = players });
            var res = await PostAsync("/api/matches/result", body, auth: true);
            return res == null ? null : JsonUtility.FromJson<StatusRes>(res)?.status;
        }

        /// <summary>Ambil papan peringkat (top rating). Endpoint anonim — tak butuh login.</summary>
        public async Task<LeaderRow[]> GetLeaderboardAsync(int limit = 20)
        {
            var res = await GetAsync($"/api/leaderboard?limit={limit}");
            if (res == null) return System.Array.Empty<LeaderRow>();
            // JsonUtility tak bisa array top-level → bungkus dulu.
            var wrap = JsonUtility.FromJson<LeaderWrap>("{\"items\":" + res + "}");
            return wrap != null && wrap.items != null ? wrap.items : System.Array.Empty<LeaderRow>();
        }

        /// <summary>Peringkat pemain sendiri (placed + rank, atau status placement).</summary>
        public async Task<MyRankRes> GetMyRankAsync()
        {
            var res = await GetAsync("/api/me/rank");
            return res == null ? null : JsonUtility.FromJson<MyRankRes>(res);
        }

        /// <summary>Riwayat match terakhir (hanya yang selesai).</summary>
        public async Task<MatchRow[]> GetMatchHistoryAsync()
        {
            var res = await GetAsync("/api/matches");
            if (res == null) return System.Array.Empty<MatchRow>();
            var wrap = JsonUtility.FromJson<MatchWrap>("{\"items\":" + res + "}");
            return wrap != null && wrap.items != null ? wrap.items : System.Array.Empty<MatchRow>();
        }

        /// <summary>Refresh rating/W-L dari server.</summary>
        public async Task<bool> RefreshMeAsync()
        {
            var res = await GetAsync("/api/me");
            if (res == null) return false;
            var dto = JsonUtility.FromJson<MeRes>(res);
            Rating = dto.rating;
            Username = string.IsNullOrEmpty(dto.username) ? Username : dto.username;
            DisplayName = dto.displayName ?? DisplayName;
            Faction = dto.faction;
            RankTier = dto.rankTier ?? "";
            AccountLevel = dto.accountLevel; AccountRank = dto.accountRank ?? ""; AccountXp = dto.accountXp;
            Wins = dto.wins; Losses = dto.losses; MatchesPlayed = dto.matchesPlayed; WinRate = dto.winRate;
            Kills = dto.kills; Deaths = dto.deaths; Assists = dto.assists; Kda = dto.kda;
            return true;
        }

        // ---------- HTTP primitif ----------
        private async Task<string> PostAsync(string path, string json, bool auth)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            return await SendAsync(req, auth);
        }

        private async Task<string> GetAsync(string path)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            return await SendAsync(req, auth: true);
        }

        private async Task<string> SendAsync(HttpRequestMessage req, bool auth)
        {
            if (auth && Token.Length > 0)
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
            try
            {
                using var res = await _http.SendAsync(req);
                var body = await res.Content.ReadAsStringAsync();
                if (!res.IsSuccessStatusCode)
                {
                    Debug.LogWarning($"[NexusBackend] {req.Method} {req.RequestUri} -> {(int)res.StatusCode}: {body}");
                    return null;
                }
                return body;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NexusBackend] {req.Method} {req.RequestUri} gagal: {e.Message}");
                return null;
            }
        }
    }
}
