using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotonBackend.Api.Data;

namespace PhotonBackend.Tests;

public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GuestLogin_RejectsShortDeviceId()
    {
        var res = await factory.CreateClient().PostAsJsonAsync("/api/auth/guest", new { deviceId = "x", displayName = "A" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task GuestLogin_IsIdempotentPerDevice()
    {
        var (_, a) = await factory.LoginAsync("device-idempotent-1", "Alice");
        var (_, b) = await factory.LoginAsync("device-idempotent-1", "Alice2");
        Assert.Equal(a.PlayerId, b.PlayerId);
        Assert.Equal("Alice2", b.DisplayName);
    }

    [Fact]
    public async Task Me_RequiresToken()
    {
        var res = await factory.CreateClient().GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task PhotonAuth_AcceptsValidToken()
    {
        var (_, login) = await factory.LoginAsync("device-photon-auth", "Bob");
        var res = await factory.CreateClient()
            .GetAsync($"/photon/auth?secret={ApiFactory.AuthSecret}&token={Uri.EscapeDataString(login.Token)}");
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("ResultCode").GetInt32());
        Assert.Equal(login.PlayerId.ToString(), json.GetProperty("UserId").GetString());
        Assert.Equal("Bob", json.GetProperty("Nickname").GetString());
    }

    [Theory]
    [InlineData("secret=wrong&token=abc", 2)]
    [InlineData("secret=test-auth-secret&token=garbage", 2)]
    [InlineData("secret=test-auth-secret", 3)]
    public async Task PhotonAuth_RejectsBadRequests(string query, int expectedCode)
    {
        var res = await factory.CreateClient().GetAsync($"/photon/auth?{query}");
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedCode, json.GetProperty("ResultCode").GetInt32());
    }

    [Fact]
    public async Task Webhook_RequiresSecret()
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/photon/webhooks/game/create", new { GameId = "room-1" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        client.DefaultRequestHeaders.Add("X-SecretKey", ApiFactory.WebhookSecret);
        res = await client.PostAsJsonAsync("/photon/webhooks/game/create", new { GameId = "room-1" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task MatchResult_BothPlayersAgree_UpdatesRatingsAndLeaderboard()
    {
        var (alice, a) = await factory.LoginAsync("device-match-alice", "MatchAlice");
        var (bob, b) = await factory.LoginAsync("device-match-bob", "MatchBob");

        var r1 = await alice.PostAsJsonAsync("/api/matches/result", new { gameId = "room-agree", slot = 0, scoreLeft = 5, scoreRight = 2 });
        Assert.Equal("Accepted", (await r1.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("status").GetString());

        var r2 = await bob.PostAsJsonAsync("/api/matches/result", new { gameId = "room-agree", slot = 1, scoreLeft = 5, scoreRight = 2 });
        Assert.Equal("Finished", (await r2.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("status").GetString());

        var meA = await alice.GetFromJsonAsync<JsonElement>("/api/me");
        var meB = await bob.GetFromJsonAsync<JsonElement>("/api/me");
        // Glicko-2: pemenang naik, pecundang turun; dari start identik perubahannya simetris (±1 pembulatan).
        var ratingA = meA.GetProperty("rating").GetInt32();
        var ratingB = meB.GetProperty("rating").GetInt32();
        Assert.True(ratingA > 1500, $"winner should rise above start, got {ratingA}");
        Assert.True(ratingB < 1500, $"loser should fall below start, got {ratingB}");
        Assert.True(Math.Abs((ratingA - 1500) - (1500 - ratingB)) <= 1, "symmetric swing from identical start");
        Assert.Equal(1, meA.GetProperty("wins").GetInt32());
        Assert.Equal(1, meB.GetProperty("losses").GetInt32());

        // Belum selesai placement (baru 1 ranked, perlu ≥5) → TIDAK tampil di leaderboard. (Bukan karena deviceId.)
        var board = await alice.GetFromJsonAsync<JsonElement>("/api/leaderboard?limit=50");
        var names = board.EnumerateArray().Select(x => x.GetProperty("displayName").GetString()).ToList();
        Assert.DoesNotContain("MatchAlice", names);
        Assert.DoesNotContain("MatchBob", names);
        // /api/me/rank: belum placement → placed=false + sisa match.
        var rank = await alice.GetFromJsonAsync<JsonElement>("/api/me/rank");
        Assert.False(rank.GetProperty("placed").GetBoolean());

        // Reporting again after finalization is rejected, so ratings cannot be farmed.
        var again = await alice.PostAsJsonAsync("/api/matches/result", new { gameId = "room-agree", slot = 0, scoreLeft = 5, scoreRight = 2 });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var history = await alice.GetFromJsonAsync<JsonElement>("/api/matches");
        Assert.Equal(1, history.GetArrayLength());
        Assert.True(history[0].GetProperty("won").GetBoolean());
    }

    [Fact]
    public async Task MatchResult_Disagreement_IsDisputedAndNotRated()
    {
        var (alice, _) = await factory.LoginAsync("device-dispute-alice", "DispAlice");
        var (bob, _) = await factory.LoginAsync("device-dispute-bob", "DispBob");

        await alice.PostAsJsonAsync("/api/matches/result", new { gameId = "room-dispute", slot = 0, scoreLeft = 5, scoreRight = 0 });
        var res = await bob.PostAsJsonAsync("/api/matches/result", new { gameId = "room-dispute", slot = 1, scoreLeft = 0, scoreRight = 5 });
        Assert.Equal("Disputed", (await res.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("status").GetString());

        var me = await alice.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(1500, me.GetProperty("rating").GetInt32());
    }

    // Payload statistik lengkap identik (deterministik) → Finished + statistik detail tersimpan.
    [Fact]
    public async Task MatchResult_FullPayloadAgrees_StoresDetailedStats()
    {
        var (alice, _) = await factory.LoginAsync("device-detail-alice", "DetailAlice");
        var (bob, _) = await factory.LoginAsync("device-detail-bob", "DetailBob");

        object[] players =
        {
            new { slot = 0, kills = 5, deaths = 2, heroId = 0, damageDealt = 320, damageTaken = 140, skillsUsed = 6, skillsHit = 4, hitsBlocked = 3, dodges = 2, guardCrushes = 1, criticals = 5, gearDrops = 7, firstBlood = true, maxKillStreak = 3, maxDeficit = 1, heroSwitches = 1,
                heroes = new[] { new { heroId = 0, playTicks = 900, kills = 4, damage = 250 }, new { heroId = 1, playTicks = 300, kills = 1, damage = 70 } } },
            new { slot = 1, kills = 2, deaths = 5, heroId = 1, damageDealt = 140, damageTaken = 320, skillsUsed = 5, skillsHit = 2, hitsBlocked = 1, dodges = 0, guardCrushes = 0, criticals = 2, gearDrops = 3, firstBlood = false, maxKillStreak = 1, maxDeficit = 3, heroSwitches = 0,
                heroes = new[] { new { heroId = 1, playTicks = 1200, kills = 2, damage = 140 } } },
        };

        await alice.PostAsJsonAsync("/api/matches/result", new { gameId = "room-detail", slot = 0, scoreLeft = 5, scoreRight = 2, kills = 5, deaths = 2, durationSec = 120, mode = "ranked", players });
        var r2 = await bob.PostAsJsonAsync("/api/matches/result", new { gameId = "room-detail", slot = 1, scoreLeft = 5, scoreRight = 2, kills = 2, deaths = 5, durationSec = 120, mode = "ranked", players });
        Assert.Equal("Finished", (await r2.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("status").GetString());

        var history = await alice.GetFromJsonAsync<JsonElement>("/api/matches");
        var m = history[0];
        Assert.True(m.GetProperty("hasDetailedStats").GetBoolean());
        var p0 = m.GetProperty("players").EnumerateArray().First(x => x.GetProperty("slot").GetInt32() == 0);
        Assert.Equal(320, p0.GetProperty("damageDealt").GetInt32());
        Assert.Equal(1, p0.GetProperty("guardCrushes").GetInt32());
        Assert.True(p0.GetProperty("firstBlood").GetBoolean());
        Assert.Equal(2, p0.GetProperty("heroes").GetArrayLength());
    }

    // Skor sama tapi statistik berbeda (klien dimodifikasi menggelembungkan) → Disputed, tidak di-rating.
    [Fact]
    public async Task MatchResult_PayloadMismatch_IsDisputed()
    {
        var (alice, _) = await factory.LoginAsync("device-mismatch-alice", "MisAlice");
        var (bob, _) = await factory.LoginAsync("device-mismatch-bob", "MisBob");

        object Row(int slot, int kills, int dmg) => new { slot, kills, deaths = 0, heroId = 0, damageDealt = dmg, damageTaken = 0, skillsUsed = 0, skillsHit = 0, hitsBlocked = 0, dodges = 0, guardCrushes = 0, criticals = 0, gearDrops = 0, firstBlood = false, maxKillStreak = 0, maxDeficit = 0, heroSwitches = 0, heroes = new object[0] };

        var honest = new[] { Row(0, 5, 300), Row(1, 2, 140) };
        var cheated = new[] { Row(0, 5, 999), Row(1, 2, 140) }; // damage digelembungkan

        await alice.PostAsJsonAsync("/api/matches/result", new { gameId = "room-mismatch", slot = 0, scoreLeft = 5, scoreRight = 2, durationSec = 120, players = cheated });
        var res = await bob.PostAsJsonAsync("/api/matches/result", new { gameId = "room-mismatch", slot = 1, scoreLeft = 5, scoreRight = 2, durationSec = 120, players = honest });
        Assert.Equal("Disputed", (await res.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("status").GetString());

        var me = await alice.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(1500, me.GetProperty("rating").GetInt32());
    }

    // docs/32 aturan 1v1: kills A = deaths B, kills tiap pemain = skor timnya (pemenang 5). Payload valid → ranked1v1.
    private static object RankedRow(int slot, int kills, int deaths) => new { slot, kills, botKills = 0, deaths, heroId = 0, damageDealt = 100, damageTaken = 100, skillsUsed = 0, skillsHit = 0, hitsBlocked = 0, dodges = 0, guardCrushes = 0, criticals = 0, gearDrops = 0, firstBlood = slot == 0, maxKillStreak = 0, maxDeficit = 0, heroSwitches = 0, heroes = new object[0] };

    [Fact]
    public async Task Ranked1v1_ValidInvariant_IsFinishedAndTagged()
    {
        var (alice, _) = await factory.LoginAsync("device-inv-ok-a", "InvOkA");
        var (bob, _) = await factory.LoginAsync("device-inv-ok-b", "InvOkB");
        // skor 5-2: pemenang slot0 kills=5 deaths=2 ; pecundang slot1 kills=2 deaths=5 (kills A = deaths B).
        var players = new[] { RankedRow(0, 5, 2), RankedRow(1, 2, 5) };
        await alice.PostAsJsonAsync("/api/matches/result", new { gameId = "room-inv-ok", slot = 0, scoreLeft = 5, scoreRight = 2, durationSec = 60, mode = "ranked", players });
        var r2 = await bob.PostAsJsonAsync("/api/matches/result", new { gameId = "room-inv-ok", slot = 1, scoreLeft = 5, scoreRight = 2, durationSec = 60, mode = "ranked", players });
        Assert.Equal("Finished", (await r2.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("status").GetString());

        var history = await alice.GetFromJsonAsync<JsonElement>("/api/matches");
        Assert.Equal("ranked1v1", history[0].GetProperty("format").GetString());
        // Poin Ladder TIDAK boleh terbalik: pemenang ratingDelta >= 0, yang kalah <= 0.
        Assert.True(history[0].GetProperty("won").GetBoolean());
        Assert.True(history[0].GetProperty("ratingDelta").GetInt32() >= 0, "pemenang harus naik rating");
        var p1 = history[0].GetProperty("players").EnumerateArray().First(x => x.GetProperty("slot").GetInt32() == 1);
        Assert.True(p1.GetProperty("ratingDelta").GetInt32() <= 0, "yang kalah harus turun rating");
    }

    [Fact]
    public async Task Ranked1v1_ViolatesInvariant_IsDisputed()
    {
        var (alice, _) = await factory.LoginAsync("device-inv-bad-a", "InvBadA");
        var (bob, _) = await factory.LoginAsync("device-inv-bad-b", "InvBadB");
        // Melanggar: p1.kills=4 tapi scoreRight=2 (kills ≠ skor) → Disputed, tak di-rating.
        var players = new[] { RankedRow(0, 5, 2), RankedRow(1, 4, 5) };
        await alice.PostAsJsonAsync("/api/matches/result", new { gameId = "room-inv-bad", slot = 0, scoreLeft = 5, scoreRight = 2, durationSec = 60, mode = "ranked", players });
        var r2 = await bob.PostAsJsonAsync("/api/matches/result", new { gameId = "room-inv-bad", slot = 1, scoreLeft = 5, scoreRight = 2, durationSec = 60, mode = "ranked", players });
        Assert.Equal("Disputed", (await r2.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("status").GetString());

        var me = await alice.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(1500, me.GetProperty("rating").GetInt32());
    }

    // ---- T2 / NX-11: waktu habis, tie-break, seri, forfeit, koTimeline (docs/90 2026-11-13) ----

    private static object[] Timeline(params int[] killerSlots) =>
        killerSlots.Select((s, i) => (object)new { tick = 300 * (i + 1), killerSlot = s }).ToArray();

    private static async Task<string> ReportBoth(HttpClient a, HttpClient b, string gameId, object body0, object body1)
    {
        await a.PostAsJsonAsync("/api/matches/result", body0);
        var r = await b.PostAsJsonAsync("/api/matches/result", body1);
        return (await r.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("status").GetString()!;
    }

    [Fact]
    public async Task TimeUp_HigherScoreWins_RatedWithTimeline()
    {
        var (alice, _) = await factory.LoginAsync("device-time-win-a", "TimeWinA");
        var (bob, _) = await factory.LoginAsync("device-time-win-b", "TimeWinB");
        var players = new[] { RankedRow(0, 2, 1), RankedRow(1, 1, 2) };
        var tl = Timeline(0, 1, 0);
        object Body(int slot) => new { gameId = "room-time-win", slot, scoreLeft = 2, scoreRight = 1, durationSec = 100, mode = "ranked", players, endReason = "time", winnerSlot = 0, koTimeline = tl };

        Assert.Equal("Finished", await ReportBoth(alice, bob, "room-time-win", Body(0), Body(1)));
        var h = (await alice.GetFromJsonAsync<JsonElement>("/api/matches"))[0];
        Assert.True(h.GetProperty("won").GetBoolean());
        Assert.False(h.GetProperty("isDraw").GetBoolean());
        Assert.Equal("time", h.GetProperty("endReason").GetString());
        Assert.Equal(3, JsonDocument.Parse(h.GetProperty("koTimeline").GetString()!).RootElement.GetArrayLength());
        Assert.True((await alice.GetFromJsonAsync<JsonElement>("/api/me")).GetProperty("rating").GetInt32() > 1500);
    }

    [Fact]
    public async Task TimeUp_TiedScore_TieBreakWinnerAccepted()
    {
        var (alice, _) = await factory.LoginAsync("device-time-tb-a", "TimeTbA");
        var (bob, _) = await factory.LoginAsync("device-time-tb-b", "TimeTbB");
        var players = new[] { RankedRow(0, 1, 1), RankedRow(1, 1, 1) };
        // Skor 1-1 → sim memutus lewat gear rontok/darah: slot 1 menang.
        object Body(int slot) => new { gameId = "room-time-tb", slot, scoreLeft = 1, scoreRight = 1, durationSec = 100, mode = "ranked", players, endReason = "time", winnerSlot = 1, koTimeline = Timeline(0, 1) };

        Assert.Equal("Finished", await ReportBoth(alice, bob, "room-time-tb", Body(0), Body(1)));
        Assert.True((await bob.GetFromJsonAsync<JsonElement>("/api/me")).GetProperty("rating").GetInt32() > 1500);
        Assert.True((await alice.GetFromJsonAsync<JsonElement>("/api/me")).GetProperty("rating").GetInt32() < 1500);
    }

    [Fact]
    public async Task TimeUp_Draw_NoRatingNoWinLoss()
    {
        var (alice, _) = await factory.LoginAsync("device-time-draw-a", "TimeDrawA");
        var (bob, _) = await factory.LoginAsync("device-time-draw-b", "TimeDrawB");
        var players = new[] { RankedRow(0, 0, 0), RankedRow(1, 0, 0) };
        object Body(int slot) => new { gameId = "room-time-draw", slot, scoreLeft = 0, scoreRight = 0, durationSec = 100, mode = "ranked", players, endReason = "time", winnerSlot = -1, koTimeline = Timeline() };

        Assert.Equal("Finished", await ReportBoth(alice, bob, "room-time-draw", Body(0), Body(1)));
        foreach (var c in new[] { alice, bob })
        {
            var me = await c.GetFromJsonAsync<JsonElement>("/api/me");
            Assert.Equal(1500, me.GetProperty("rating").GetInt32());
            Assert.Equal(0, me.GetProperty("wins").GetInt32());
            Assert.Equal(0, me.GetProperty("losses").GetInt32());
        }
        var h = (await alice.GetFromJsonAsync<JsonElement>("/api/matches"))[0];
        Assert.True(h.GetProperty("isDraw").GetBoolean());
        Assert.False(h.GetProperty("won").GetBoolean());
        Assert.Equal(0, h.GetProperty("ratingDelta").GetInt32());

        var stats = await alice.GetFromJsonAsync<JsonElement>("/api/me/stats");
        Assert.Equal(1, stats.GetProperty("draws").GetInt32());
        Assert.Equal(0, stats.GetProperty("losses").GetInt32()); // seri BUKAN kalah
    }

    [Fact]
    public async Task Forfeit_AfkLoses_WinnerRated()
    {
        var (alice, _) = await factory.LoginAsync("device-forfeit-a", "ForfeitA");
        var (bob, _) = await factory.LoginAsync("device-forfeit-b", "ForfeitB");
        var players = new[] { RankedRow(0, 1, 0), RankedRow(1, 0, 1) };
        // Slot 0 AFK 20 dtk → forfeit, slot 1 menang walau skor 1-0 untuk slot 0.
        object Body(int slot) => new { gameId = "room-forfeit", slot, scoreLeft = 1, scoreRight = 0, durationSec = 40, mode = "ranked", players, endReason = "forfeit", winnerSlot = 1, koTimeline = Timeline(0) };

        Assert.Equal("Finished", await ReportBoth(alice, bob, "room-forfeit", Body(0), Body(1)));
        var h = (await bob.GetFromJsonAsync<JsonElement>("/api/matches"))[0];
        Assert.True(h.GetProperty("won").GetBoolean());
        Assert.Equal("forfeit", h.GetProperty("endReason").GetString());
    }

    [Fact]
    public async Task KoTimeline_Mismatch_IsDisputed()
    {
        var (alice, _) = await factory.LoginAsync("device-tl-mis-a", "TlMisA");
        var (bob, _) = await factory.LoginAsync("device-tl-mis-b", "TlMisB");
        var players = new[] { RankedRow(0, 5, 2), RankedRow(1, 2, 5) };
        var tlA = Timeline(0, 0, 1, 0, 1, 0, 0);
        var tlB = Timeline(0, 1, 0, 0, 1, 0, 0); // urutan KO berbeda → simulasi tidak sama
        await alice.PostAsJsonAsync("/api/matches/result", new { gameId = "room-tl-mis", slot = 0, scoreLeft = 5, scoreRight = 2, durationSec = 90, mode = "ranked", players, endReason = "ko", koTimeline = tlA });
        var r = await bob.PostAsJsonAsync("/api/matches/result", new { gameId = "room-tl-mis", slot = 1, scoreLeft = 5, scoreRight = 2, durationSec = 90, mode = "ranked", players, endReason = "ko", koTimeline = tlB });
        Assert.Equal("Disputed", (await r.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("status").GetString());
    }

    public static IEnumerable<object[]> IllegalT2 => new[]
    {
        new object[] { "time", 5, 2, 0, null! },               // waktu habis tapi sudah capai WinScore
        new object[] { "time", 2, 1, 1, null! },               // skor beda, pemenang bukan skor tertinggi
        new object[] { "time", 1, 1, null!, null! },           // waktu habis tanpa winnerSlot
        new object[] { "forfeit", 1, 0, 2, null! },            // slot pemenang tak valid
        new object[] { "draw-please", 1, 0, 0, null! },        // sebab akhir tak dikenal
        new object[] { "ko", 5, 2, 0, new[] { 0, 0, 0, 0, 1, 1 } }, // timeline kurang 1 KO
        new object[] { "ko", 5, 2, 0, new[] { 0, 0, 0, 0, 0, 0, 1 } }, // KO per slot ≠ skor
    };

    [Theory]
    [MemberData(nameof(IllegalT2))]
    public async Task MatchResult_RejectsIllegalT2Results(string endReason, int left, int right, int? winnerSlot, int[]? killers)
    {
        var (alice, _) = await factory.LoginAsync($"device-illegal-t2-{endReason}-{left}{right}{winnerSlot}{killers?.Length}", "IllT2");
        var res = await alice.PostAsJsonAsync("/api/matches/result", new
        {
            gameId = $"room-illegal-t2-{endReason}-{left}{right}{winnerSlot}{killers?.Length}{killers?.Sum()}", slot = 0,
            scoreLeft = left, scoreRight = right, endReason, winnerSlot,
            koTimeline = killers is null ? null : Timeline(killers),
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // Durasi mustahil pendek → sanity-check menolak (Disputed).
    [Fact]
    public async Task MatchResult_FailsSanityCheck_IsDisputed()
    {
        var (alice, _) = await factory.LoginAsync("device-sanity-alice", "SanAlice");
        var (bob, _) = await factory.LoginAsync("device-sanity-bob", "SanBob");

        await alice.PostAsJsonAsync("/api/matches/result", new { gameId = "room-sanity", slot = 0, scoreLeft = 5, scoreRight = 2, durationSec = 2 });
        var res = await bob.PostAsJsonAsync("/api/matches/result", new { gameId = "room-sanity", slot = 1, scoreLeft = 5, scoreRight = 2, durationSec = 2 });
        Assert.Equal("Disputed", (await res.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("status").GetString());

        var me = await alice.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(1500, me.GetProperty("rating").GetInt32());
    }

    [Theory]
    [InlineData(3, 2)]  // nobody reached the win score
    [InlineData(5, 5)]  // tie
    [InlineData(6, 0)]  // above win score
    [InlineData(-1, 5)] // negative
    public async Task MatchResult_RejectsIllegalScores(int left, int right)
    {
        var (alice, _) = await factory.LoginAsync("device-illegal-alice", "IllAlice");
        var res = await alice.PostAsJsonAsync("/api/matches/result", new { gameId = $"room-illegal-{left}-{right}", slot = 0, scoreLeft = left, scoreRight = right });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task MatchResult_SameSlotCannotBeClaimedTwice()
    {
        var (alice, _) = await factory.LoginAsync("device-slot-alice", "SlotAlice");
        var (bob, _) = await factory.LoginAsync("device-slot-bob", "SlotBob");

        await alice.PostAsJsonAsync("/api/matches/result", new { gameId = "room-slot", slot = 0, scoreLeft = 5, scoreRight = 1 });
        var res = await bob.PostAsJsonAsync("/api/matches/result", new { gameId = "room-slot", slot = 0, scoreLeft = 5, scoreRight = 1 });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Matchmaking_PairsTwoPlayers_SameGameOppositeSlots()
    {
        var (alice, _) = await factory.LoginAsync("device-mm-alice", "MmAlice");
        var (bob, _) = await factory.LoginAsync("device-mm-bob", "MmBob");

        var r1 = await alice.PostAsJsonAsync("/api/matchmaking/join", new { region = "asia" });
        Assert.Equal("Searching", (await r1.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("state").GetString());

        var r2 = await bob.PostAsJsonAsync("/api/matchmaking/join", new { region = "asia" });
        var m2 = await r2.Content.ReadFromJsonAsync<JsonElement>(Web);
        Assert.Equal("Matched", m2.GetProperty("state").GetString());
        var gameId = m2.GetProperty("gameId").GetString();
        Assert.False(string.IsNullOrEmpty(gameId));

        var s1 = await alice.GetFromJsonAsync<JsonElement>("/api/matchmaking/status");
        Assert.Equal("Matched", s1.GetProperty("state").GetString());
        Assert.Equal(gameId, s1.GetProperty("gameId").GetString());
        Assert.NotEqual(s1.GetProperty("slot").GetInt32(), m2.GetProperty("slot").GetInt32());
    }

    [Fact]
    public async Task NewGuest_AnyDeviceId_DefaultsNotTestAccount()
    {
        // Login asli (mis. Facebook nanti) dgn deviceId "fb-..." TANPA flag isTest → BUKAN akun uji.
        var (_, login) = await factory.LoginAsync("fb-real-user-123", "RealFbPlayer", isTest: false);
        Assert.False(await factory.IsTestAccountAsync(login.PlayerId));
    }

    [Fact]
    public async Task TestPathAccount_IsFlaggedExplicitly()
    {
        var (_, login) = await factory.LoginAsync("device-whatever", "SomeTestAcc"); // isTest default true (jalur tes)
        Assert.True(await factory.IsTestAccountAsync(login.PlayerId));
    }

    private async Task<HttpClient> RegisterAsync(string username, string password, string? name = null)
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/register", new { username, password, displayName = name ?? username });
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadFromJsonAsync<JsonElement>(Web);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
        return client;
    }

    [Fact]
    public async Task Register_Then_Me_ReturnsProfileWithRankTier()
    {
        var client = await RegisterAsync("acct_alice", "secret123", "AliceA");
        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal("acct_alice", me.GetProperty("username").GetString());
        Assert.Equal("AliceA", me.GetProperty("displayName").GetString());
        Assert.Equal("Rival", me.GetProperty("rankTier").GetString());       // start 1500 = Rival (doc 24)
        Assert.Equal("Trainee", me.GetProperty("accountRank").GetString());  // 0 XP
        Assert.Equal(0, me.GetProperty("matchesPlayed").GetInt32());
        Assert.Equal(0, me.GetProperty("kills").GetInt32());
        Assert.Equal(0, me.GetProperty("faction").GetInt32());
    }

    [Fact]
    public async Task Register_DuplicateUsername_Conflict()
    {
        await RegisterAsync("dup_user", "secret123");
        var res = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/register", new { username = "dup_user", password = "another123", displayName = "X" });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Login_CorrectAndWrongPassword()
    {
        await RegisterAsync("login_bob", "rightpass1");
        var client = factory.CreateClient();
        var ok = await client.PostAsJsonAsync("/api/auth/login", new { username = "login_bob", password = "rightpass1" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var bad = await client.PostAsJsonAsync("/api/auth/login", new { username = "login_bob", password = "wrongpass" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
    }

    [Fact]
    public async Task Register_ShortPassword_BadRequest()
    {
        var res = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/register", new { username = "shortpw_user", password = "123", displayName = "X" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task SetFaction_UpdatesProfile()
    {
        var client = await RegisterAsync("faction_user", "secret123");
        var res = await client.PostAsJsonAsync("/api/me/faction", new { faction = 1 });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(1, me.GetProperty("faction").GetInt32());
    }

    [Fact]
    public async Task SetFaction_OneFreeSwitch_ThenLocked_RejectsInvalid()
    {
        var client = await RegisterAsync("faction_lock", "secret123");
        var first = await client.PostAsJsonAsync("/api/me/faction", new { faction = 2 });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var st = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(st.GetProperty("switchAvailable").GetBoolean());   // placement 0 → jatah pindah masih ada

        // Pindah PERTAMA (placement <5) → GRATIS, diterima; jatah habis.
        var sw = await client.PostAsJsonAsync("/api/me/faction", new { faction = 1 });
        Assert.Equal(HttpStatusCode.OK, sw.StatusCode);
        Assert.True((await sw.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("locked").GetBoolean());

        // Pindah KEDUA → DITOLAK (jatah gratis sudah dipakai).
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/me/faction", new { faction = 2 })).StatusCode);

        // Pilih faksi yang SAMA → idempoten OK.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/me/faction", new { faction = 1 })).StatusCode);

        // 0 (batal) / di luar 1-2 → 400.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/me/faction", new { faction = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/me/faction", new { faction = 3 })).StatusCode);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(1, me.GetProperty("faction").GetInt32());
    }

    [Fact]
    public async Task SetFaction_SwitchLocked_AfterFivePlacementMatches()
    {
        var (client, login) = await factory.LoginAsync("device-fac-placed", "FacPlaced");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/me/faction", new { faction = 1 })).StatusCode);

        await factory.SeedFinishedRankedAsync(login.PlayerId, 5);   // placement selesai (= syarat leaderboard)

        // Jatah gratis BELUM dipakai, tapi placement selesai → tetap terkunci.
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/me/faction", new { faction = 2 })).StatusCode);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(1, me.GetProperty("faction").GetInt32());
    }

    [Fact]
    public async Task SetFaction_FourPlacementMatches_SwitchStillFree()
    {
        var (client, login) = await factory.LoginAsync("device-fac-four", "FacFour");
        await client.PostAsJsonAsync("/api/me/faction", new { faction = 1 });
        await factory.SeedFinishedRankedAsync(login.PlayerId, 4);   // batas: 4 < 5 → masih boleh
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/me/faction", new { faction = 2 })).StatusCode);
    }

    [Fact]
    public async Task AdminSetFaction_BypassesLock_RequiresSecret()
    {
        var (client, login) = await factory.LoginAsync("device-admin-fac", "AdminFac");
        await client.PostAsJsonAsync("/api/me/faction", new { faction = 1 }); // terkunci ke ORDE

        // Tanpa header admin → 401.
        var noSecret = factory.CreateClient();
        var r401 = await noSecret.PostAsJsonAsync("/api/admin/faction", new { playerId = login.PlayerId, faction = 0 });
        Assert.Equal(HttpStatusCode.Unauthorized, r401.StatusCode);

        // Dengan header admin → reset faksi ke 0 (lewati kunci).
        var adminC = factory.CreateClient();
        adminC.DefaultRequestHeaders.Add("X-Admin-Secret", ApiFactory.AdminSecret);
        var rOk = await adminC.PostAsJsonAsync("/api/admin/faction", new { playerId = login.PlayerId, faction = 0, adminSource = "unit-test" });
        Assert.Equal(HttpStatusCode.OK, rOk.StatusCode);

        // Setiap perubahan admin tercatat di audit (lama→baru, sumber). Reset ORDE(1) → none(0).
        var audits = await factory.FactionAuditsAsync(login.PlayerId);
        var reset = Assert.Single(audits);
        Assert.Equal(1, reset.OldFaction);
        Assert.Equal(0, reset.NewFaction);
        Assert.Contains("unit-test", reset.AdminSource);

        // Pemain kini bisa memilih lagi (kunci lepas).
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/me/faction", new { faction = 2 })).StatusCode);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(2, me.GetProperty("faction").GetInt32());
    }

    [Fact]
    public async Task AdminSetFaction_RestoresFreeSwitch()
    {
        var (client, login) = await factory.LoginAsync("device-admin-sw", "AdminSw");
        await client.PostAsJsonAsync("/api/me/faction", new { faction = 1 });
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/me/faction", new { faction = 2 })).StatusCode); // pakai jatah
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/me/faction", new { faction = 1 })).StatusCode);

        var adminC = factory.CreateClient();
        adminC.DefaultRequestHeaders.Add("X-Admin-Secret", ApiFactory.AdminSecret);
        Assert.Equal(HttpStatusCode.OK, (await adminC.PostAsJsonAsync("/api/admin/faction", new { playerId = login.PlayerId, faction = 2 })).StatusCode);

        // Jatah pindah gratis dikembalikan oleh reset admin.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/me/faction", new { faction = 1 })).StatusCode);
    }

    [Fact]
    public async Task Factions_ReportsPopulation()
    {
        var client = await RegisterAsync("faction_pop", "secret123");
        await client.PostAsJsonAsync("/api/me/faction", new { faction = 1 });

        var f = await client.GetFromJsonAsync<JsonElement>("/api/factions");
        Assert.True(f.GetProperty("orde").GetInt32() >= 1);
        Assert.True(f.TryGetProperty("needsWarriors", out _));
        Assert.True(f.TryGetProperty("bonusPercent", out _));
    }

    [Fact]
    public async Task MatchReport_AccumulatesKillsDeathsKdaAndXp()
    {
        var (alice, _) = await factory.LoginAsync("device-stat-alice", "StatAlice");
        var (bob, _) = await factory.LoginAsync("device-stat-bob", "StatBob");

        await alice.PostAsJsonAsync("/api/matches/result", new { gameId = "room-stat", slot = 0, scoreLeft = 5, scoreRight = 2, kills = 5, deaths = 2, assists = 1 });
        var r2 = await bob.PostAsJsonAsync("/api/matches/result", new { gameId = "room-stat", slot = 1, scoreLeft = 5, scoreRight = 2, kills = 2, deaths = 5, assists = 0 });
        Assert.Equal("Finished", (await r2.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("status").GetString());

        var meA = await alice.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(5, meA.GetProperty("kills").GetInt32());
        Assert.Equal(2, meA.GetProperty("deaths").GetInt32());
        Assert.Equal(1, meA.GetProperty("wins").GetInt32());
        Assert.Equal(3.0, meA.GetProperty("kda").GetDouble());          // (5+1)/2
        Assert.True(meA.GetProperty("accountXp").GetInt32() >= 100);    // menang → +100

        var meB = await bob.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(2, meB.GetProperty("kills").GetInt32());
        Assert.Equal(5, meB.GetProperty("deaths").GetInt32());
    }

    [Fact]
    public async Task Health_IsOk()
    {
        var res = await factory.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    // ---- Poin d / docs/31 §1.7: Pengumuman lobby ----
    [Fact]
    public async Task Announcements_AdminCreates_PublicSeesOnlyActive_RemoveHides()
    {
        var anon = factory.CreateClient();
        var noAdmin = await anon.PostAsJsonAsync("/api/admin/announcements", new { kind = "info", title = "x" });
        Assert.Equal(HttpStatusCode.Unauthorized, noAdmin.StatusCode);

        var adminC = factory.CreateClient();
        adminC.DefaultRequestHeaders.Add("X-Admin-Secret", ApiFactory.AdminSecret);
        var now = DateTime.UtcNow;
        var live = await adminC.PostAsJsonAsync("/api/admin/announcements", new { kind = "event", title = "Ann-Live", body = "Double XP akhir pekan", adminSource = "unit-test" });
        Assert.Equal(HttpStatusCode.Created, live.StatusCode);
        var liveId = (await live.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("id").GetGuid();
        await adminC.PostAsJsonAsync("/api/admin/announcements", new { kind = "info", title = "Ann-Future", startsAt = now.AddDays(1) });
        await adminC.PostAsJsonAsync("/api/admin/announcements", new { kind = "info", title = "Ann-Expired", startsAt = now.AddDays(-2), endsAt = now.AddDays(-1) });

        // Validasi
        Assert.Equal(HttpStatusCode.BadRequest, (await adminC.PostAsJsonAsync("/api/admin/announcements", new { kind = "promo", title = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await adminC.PostAsJsonAsync("/api/admin/announcements", new { kind = "info", title = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await adminC.PostAsJsonAsync("/api/admin/announcements", new { kind = "info", title = "x", startsAt = now, endsAt = now.AddMinutes(-1) })).StatusCode);

        var list = await anon.GetFromJsonAsync<JsonElement>("/api/announcements");
        var titles = list.EnumerateArray().Select(x => x.GetProperty("title").GetString()).ToList();
        Assert.Contains("Ann-Live", titles);
        Assert.DoesNotContain("Ann-Future", titles);
        Assert.DoesNotContain("Ann-Expired", titles);

        Assert.Equal(HttpStatusCode.NoContent, (await adminC.DeleteAsync($"/api/admin/announcements/{liveId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminC.DeleteAsync($"/api/admin/announcements/{liveId}")).StatusCode);
        list = await anon.GetFromJsonAsync<JsonElement>("/api/announcements");
        Assert.DoesNotContain("Ann-Live", list.EnumerateArray().Select(x => x.GetProperty("title").GetString()));
    }

    // ---- K-10: guest → akun, progres terbawa ----
    [Fact]
    public async Task UpgradeGuest_KeepsFactionRatingHistory_AndLocksDeviceLogin()
    {
        var (guest, g) = await factory.LoginAsync("device-upgrade-guest-01", "UpGuest");
        Assert.Equal(HttpStatusCode.OK, (await guest.PostAsJsonAsync("/api/me/faction", new { faction = 2 })).StatusCode);
        await factory.SeedFinishedRankedAsync(g.PlayerId, 2);

        var up = await guest.PostAsJsonAsync("/api/auth/upgrade", new { username = "upgraded_user", password = "secret123", deviceId = "device-upgrade-guest-01" });
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        var body = await up.Content.ReadFromJsonAsync<JsonElement>(Web);
        Assert.Equal(g.PlayerId, body.GetProperty("playerId").GetGuid());   // Player SAMA
        Assert.Equal(2, body.GetProperty("faction").GetInt32());            // faksi terbawa
        Assert.Equal("UpGuest", body.GetProperty("displayName").GetString());

        // Login username → akun yang sama, riwayat tetap.
        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = "upgraded_user", password = "secret123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(g.PlayerId, (await login.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("playerId").GetGuid());
        var rank = await guest.GetFromJsonAsync<JsonElement>("/api/me/rank");
        Assert.Equal(2, rank.GetProperty("rankedMatches").GetInt32());

        // Upgrade kedua ditolak; deviceId lama tak lagi membuka akun berpassword.
        Assert.Equal(HttpStatusCode.Conflict, (await guest.PostAsJsonAsync("/api/auth/upgrade", new { username = "other_name", password = "secret123", deviceId = "device-upgrade-guest-01" })).StatusCode);
        var (_, again) = await factory.LoginAsync("device-upgrade-guest-01", "UpGuest");
        Assert.NotEqual(g.PlayerId, again.PlayerId);
    }

    [Fact]
    public async Task UpgradeGuest_RejectsTakenUsername_AndBadInput()
    {
        var reg = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", new { username = "taken_name", password = "secret123" });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        var (guest, _) = await factory.LoginAsync("device-upgrade-guest-02", "UpGuest2");
        Assert.Equal(HttpStatusCode.Conflict, (await guest.PostAsJsonAsync("/api/auth/upgrade", new { username = "taken_name", password = "secret123", deviceId = "device-upgrade-guest-02" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.PostAsJsonAsync("/api/auth/upgrade", new { username = "ab", password = "secret123", deviceId = "device-upgrade-guest-02" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.PostAsJsonAsync("/api/auth/upgrade", new { username = "valid_name", password = "123", deviceId = "device-upgrade-guest-02" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsJsonAsync("/api/auth/upgrade", new { username = "x_name", password = "secret123" })).StatusCode);
    }

    // Q53: FLAWLESS hanya untuk menang KO dengan lawan 0 — menang waktu habis 1-0 / WO bukan flawless.
    [Fact]
    public async Task Flawless_OnlyForKoWins()
    {
        var (alice, _) = await factory.LoginAsync("device-flaw-a", "FlawA");
        var (bob, _) = await factory.LoginAsync("device-flaw-b", "FlawB");
        var t = new[] { RankedRow(0, 1, 0), RankedRow(1, 0, 1) };
        object Time(int slot) => new { gameId = "room-flaw-time", slot, scoreLeft = 1, scoreRight = 0, durationSec = 100, mode = "ranked", players = t, endReason = "time", winnerSlot = 0, koTimeline = Timeline(0) };
        Assert.Equal("Finished", await ReportBoth(alice, bob, "room-flaw-time", Time(0), Time(1)));
        Assert.Equal(0, (await alice.GetFromJsonAsync<JsonElement>("/api/me/stats")).GetProperty("flawless").GetInt32());

        var k = new[] { RankedRow(0, 5, 0), RankedRow(1, 0, 5) };
        object Ko(int slot) => new { gameId = "room-flaw-ko", slot, scoreLeft = 5, scoreRight = 0, durationSec = 60, mode = "ranked", players = k, endReason = "ko", koTimeline = Timeline(0, 0, 0, 0, 0) };
        Assert.Equal("Finished", await ReportBoth(alice, bob, "room-flaw-ko", Ko(0), Ko(1)));
        Assert.Equal(1, (await alice.GetFromJsonAsync<JsonElement>("/api/me/stats")).GetProperty("flawless").GetInt32());
    }

    // ---- D12: kebijakan RD (lantai 30, inflasi pasif 7 hari, anti-kolusi tak menurunkan RD) ----
    private async Task SetRdAsync(Guid id, double rd, DateTime? lastRatedAt)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var p = await db.Players.FindAsync(id);
        p!.Rd = rd; p.LastRatedAt = lastRatedAt;
        await db.SaveChangesAsync();
    }
    private async Task<double> RdAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Players.AsNoTracking().FirstAsync(x => x.Id == id)).Rd;
    }
    private async Task PlayKo(HttpClient a, HttpClient b, string room)
    {
        var rows = new[] { RankedRow(0, 5, 2), RankedRow(1, 2, 5) };
        object Body(int slot) => new { gameId = room, slot, scoreLeft = 5, scoreRight = 2, durationSec = 90, mode = "ranked", players = rows, endReason = "ko" };
        Assert.Equal("Finished", await ReportBoth(a, b, room, Body(0), Body(1)));
    }

    [Fact]
    public async Task Rd_NeverBelowFloor30()
    {
        var (a, la) = await factory.LoginAsync("device-rdfloor-a", "RdFloorA");
        var (b, lb) = await factory.LoginAsync("device-rdfloor-b", "RdFloorB");
        await SetRdAsync(la.PlayerId, 30.5, DateTime.UtcNow); await SetRdAsync(lb.PlayerId, 30.5, DateTime.UtcNow);
        await PlayKo(a, b, "room-rdfloor");
        Assert.True(await RdAsync(la.PlayerId) >= 30.0);
        Assert.True(await RdAsync(lb.PlayerId) >= 30.0);
    }

    [Fact]
    public async Task Rd_InactivePlayerEndsLessCertain()
    {
        var (a1, w1) = await factory.LoginAsync("device-rdin-a1", "RdInA1"); var (b1, o1) = await factory.LoginAsync("device-rdin-b1", "RdInB1");
        var (a2, w2) = await factory.LoginAsync("device-rdin-a2", "RdInA2"); var (b2, o2) = await factory.LoginAsync("device-rdin-b2", "RdInB2");
        var now = DateTime.UtcNow;
        await SetRdAsync(w1.PlayerId, 60, now.AddDays(-28)); await SetRdAsync(o1.PlayerId, 60, now); // w1 pasif 4 periode
        await SetRdAsync(w2.PlayerId, 60, now);              await SetRdAsync(o2.PlayerId, 60, now); // w2 aktif
        await PlayKo(a1, b1, "room-rdin-1"); await PlayKo(a2, b2, "room-rdin-2");
        Assert.True(await RdAsync(w1.PlayerId) > await RdAsync(w2.PlayerId), "pemain pasif harus lebih tak pasti");
    }

    [Fact]
    public async Task Rd_DampedCollusionMatch_DoesNotLowerRd()
    {
        var (a, la) = await factory.LoginAsync("device-rdcol-a", "RdColA");
        var (b, lb) = await factory.LoginAsync("device-rdcol-b", "RdColB");
        for (int i = 0; i < 3; i++) await PlayKo(a, b, $"room-rdcol-{i}");   // 3 pertemuan → berikutnya diredam
        double before = await RdAsync(la.PlayerId);
        await PlayKo(a, b, "room-rdcol-3");
        Assert.Equal(before, await RdAsync(la.PlayerId), 6);
    }

    // D13: badge dari ranked1v1 + battle saja; format lama (legacy5v5 = ranked tanpa payload) tidak dihitung.
    [Fact]
    public async Task Badges_IgnoreLegacyFormat()
    {
        var (a, _) = await factory.LoginAsync("device-d13-a", "D13A");
        var (b, _) = await factory.LoginAsync("device-d13-b", "D13B");
        object Legacy(int slot) => new { gameId = "room-d13-legacy", slot, scoreLeft = 5, scoreRight = 0, durationSec = 60, mode = "ranked" };
        Assert.Equal("Finished", await ReportBoth(a, b, "room-d13-legacy", Legacy(0), Legacy(1)));
        Assert.Equal("legacy5v5", (await a.GetFromJsonAsync<JsonElement>("/api/matches"))[0].GetProperty("format").GetString());
        Assert.Equal(0, (await a.GetFromJsonAsync<JsonElement>("/api/me/stats")).GetProperty("flawless").GetInt32());

        object Battle(int slot) => new { gameId = "room-d13-battle", slot, scoreLeft = 5, scoreRight = 0, durationSec = 60, mode = "battle" };
        Assert.Equal("Finished", await ReportBoth(a, b, "room-d13-battle", Battle(0), Battle(1)));
        Assert.Equal(1, (await a.GetFromJsonAsync<JsonElement>("/api/me/stats")).GetProperty("flawless").GetInt32());
    }

    // Q62/Q63: upgrade butuh deviceId guest yang cocok; nama tampil tidak ikut diganti.
    [Fact]
    public async Task UpgradeGuest_RequiresMatchingDevice_AndKeepsName()
    {
        var (guest, g) = await factory.LoginAsync("device-upgrade-guest-03", "AsliNama");
        var wrong = await guest.PostAsJsonAsync("/api/auth/upgrade", new { username = "thief_name", password = "secret123", deviceId = "device-attacker-xx" });
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        var none = await guest.PostAsJsonAsync("/api/auth/upgrade", new { username = "thief_name", password = "secret123" });
        Assert.Equal(HttpStatusCode.Forbidden, none.StatusCode);

        var ok = await guest.PostAsJsonAsync("/api/auth/upgrade", new { username = "owner_name", password = "secret123", deviceId = "device-upgrade-guest-03", displayName = "PemainTerkenal" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("AsliNama", (await ok.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("displayName").GetString());
    }


    // D14: nama unik case-insensitive + terlarang saat daftar; guest tidak pernah gagal karena nama.
    [Fact]
    public async Task DisplayName_UniqueCaseInsensitive_AndReserved()
    {
        var c = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/auth/register", new { username = "d14_first", password = "secret123", displayName = "PendekarRio" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/auth/register", new { username = "d14_second", password = "secret123", displayName = "pendekarRIO" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/auth/register", new { username = "d14_third", password = "secret123", displayName = "GM_Rio" })).StatusCode);

        // Guest meminta nama yang sudah dipakai / terlarang → tetap masuk, dengan nama lain.
        var (_, g1) = await factory.LoginAsync("device-d14-guest-1", "PENDEKARRIO");
        Assert.NotEqual("pendekarrio", g1.DisplayName.ToLowerInvariant());
        var (_, g2) = await factory.LoginAsync("device-d14-guest-2", "Admin");
        Assert.NotEqual("admin", g2.DisplayName.ToLowerInvariant());
        Assert.StartsWith("Guest", g2.DisplayName);
    }

    // NX-21: match Disputed tampil di Riwayat (includeDisputed=true) sebagai "TIDAK DIHITUNG" + alasan; default tetap disembunyikan.
    [Fact]
    public async Task History_IncludeDisputed_ShowsReason_DefaultHides()
    {
        var (a, _) = await factory.LoginAsync("device-nx21-a", "Nx21A");
        var (b, _) = await factory.LoginAsync("device-nx21-b", "Nx21B");
        await a.PostAsJsonAsync("/api/matches/result", new { gameId = "room-nx21", slot = 0, scoreLeft = 5, scoreRight = 0 });
        var r = await b.PostAsJsonAsync("/api/matches/result", new { gameId = "room-nx21", slot = 1, scoreLeft = 0, scoreRight = 5 });
        Assert.Equal("Disputed", (await r.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("status").GetString());

        Assert.Equal(0, (await a.GetFromJsonAsync<JsonElement>("/api/matches")).GetArrayLength());
        var all = await a.GetFromJsonAsync<JsonElement>("/api/matches?includeDisputed=true");
        var m = Assert.Single(all.EnumerateArray());
        Assert.Equal("Disputed", m.GetProperty("status").GetString());
        Assert.Equal("mismatch", m.GetProperty("disputeReason").GetString());
        Assert.Equal(0, m.GetProperty("ratingDelta").GetInt32());
    }
}
