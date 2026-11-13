using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PhotonBackend.Api.Auth;
using PhotonBackend.Api.Data;
using PhotonBackend.Api.Services;

namespace PhotonBackend.Api.Endpoints;

public sealed record GuestLoginRequest(string? DeviceId, string? DisplayName, bool? IsTest = null);
public sealed record RegisterRequest(string? Username, string? Password, string? DisplayName, bool? IsTest = null);
public sealed record LoginRequest(string? Username, string? Password);
public sealed record FactionRequest(int Faction);
public sealed record AdminFactionRequest(Guid PlayerId, int Faction, string? AdminSource = null);
public sealed record ReportResultRequest(string? GameId, int Slot, int ScoreLeft, int ScoreRight, int Kills = 0, int Deaths = 0, int Assists = 0, int HeroId = 0, string? Mode = null, int DurationSec = 0, int PartySize = 1, List<ReportPlayer>? Players = null,
    string? EndReason = null, int? WinnerSlot = null, List<KoEvent>? KoTimeline = null); // T2: sebab akhir, pemenang (-1 seri), jalannya match
public sealed record JoinQueueRequest(string? Region, string? BuildVersion = null, string? SimVersion = null);

/// <summary>REST API used by the Unity client.</summary>
public static class ApiEndpoints
{
    public static void MapApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/auth/guest", GuestLogin);
        api.MapPost("/auth/register", Register);
        api.MapPost("/auth/login", Login);

        var secured = api.MapGroup("").RequireAuthorization();
        secured.MapGet("/me", Me);
        secured.MapPost("/me/faction", SetFaction);
        secured.MapGet("/matches", MyMatches);
        secured.MapGet("/me/rank", MyRank);
        secured.MapPost("/matches/result", ReportResult);
        secured.MapPost("/matchmaking/join", JoinQueue);
        secured.MapGet("/matchmaking/status", QueueStatus);
        secured.MapPost("/matchmaking/leave", LeaveQueue);

        api.MapGet("/leaderboard", Leaderboard);
        api.MapGet("/factions", GetFactions); // populasi + "Butuh Pejuang" (publik: layar onboarding)

        // Admin/dev (header X-Admin-Secret = Admin:Secret). Belum ada sistem season → reset faksi manual.
        var admin = api.MapGroup("/admin").AddEndpointFilter(RequireAdminSecret);
        admin.MapPost("/faction", AdminSetFaction);
        api.MapAnnouncements(admin); // GET /announcements (publik) + admin POST/DELETE

        secured.MapStats(api); // /me/stats (auth) + /players/{id}/stats (anon)
    }

    private static object AuthPayload(Player p, TokenService tokens) => new
    {
        playerId = p.Id,
        username = p.Username,
        displayName = p.DisplayName,
        faction = p.Faction,
        rating = p.Rating,
        token = tokens.Create(p.Id, p.DisplayName),
    };

    private static async Task<IResult> Register(
        RegisterRequest req, AppDbContext db, TokenService tokens, PasswordHasher<Player> hasher,
        IOptions<GameOptions> game, CancellationToken ct)
    {
        var username = req.Username?.Trim() ?? "";
        var password = req.Password ?? "";
        var name = req.DisplayName?.Trim() is { Length: > 0 } dn ? dn : username;
        if (username.Length is < 3 or > 24) return Results.BadRequest(new { error = "username must be 3-24 characters." });
        if (password.Length is < 6 or > 128) return Results.BadRequest(new { error = "password must be 6-128 characters." });
        if (name.Length is < 1 or > 24) return Results.BadRequest(new { error = "displayName must be 1-24 characters." });

        if (await db.Players.AnyAsync(p => p.Username == username, ct))
            return Results.Conflict(new { error = "username already taken." });

        var player = new Player
        {
            Username = username, DisplayName = name,
            Rating = game.Value.StartRating, Rd = game.Value.StartRd, Volatility = game.Value.StartVolatility,
            IsTestAccount = game.Value.AllowTestAccounts && (req.IsTest ?? false), // eksplisit; default false
        };
        player.PasswordHash = hasher.HashPassword(player, password);
        db.Players.Add(player);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Results.Conflict(new { error = "username already taken." }); }

        return Results.Ok(AuthPayload(player, tokens));
    }

    private static async Task<IResult> Login(
        LoginRequest req, AppDbContext db, TokenService tokens, PasswordHasher<Player> hasher, CancellationToken ct)
    {
        var username = req.Username?.Trim() ?? "";
        var password = req.Password ?? "";
        if (username.Length == 0 || password.Length == 0)
            return Results.BadRequest(new { error = "username and password are required." });

        var player = await db.Players.FirstOrDefaultAsync(p => p.Username == username, ct);
        if (player is null || player.PasswordHash is null)
            return Results.Json(new { error = "invalid credentials." }, statusCode: StatusCodes.Status401Unauthorized);

        var ok = hasher.VerifyHashedPassword(player, player.PasswordHash, password);
        if (ok == PasswordVerificationResult.Failed)
            return Results.Json(new { error = "invalid credentials." }, statusCode: StatusCodes.Status401Unauthorized);

        return Results.Ok(AuthPayload(player, tokens));
    }

    // Pilih faksi (docs/35): 1 ORDE / 2 LEGION. Guest ikut memilih. Pilihan pertama selalu boleh.
    // PINDAH faksi: GRATIS 1× selama placement ranked belum selesai (<5, sama dgn syarat leaderboard); setelah itu terkunci.
    private static async Task<IResult> SetFaction(
        FactionRequest req, ClaimsPrincipal user, AppDbContext db, FactionService factions, CancellationToken ct)
    {
        if (req.Faction is < 1 or > 2)
            return Results.BadRequest(new { error = "faction harus 1 (ORDE) atau 2 (LEGION)." });
        var id = PlayerId(user);
        var p = await db.Players.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Results.NotFound();

        int rm = await RankedMatchesAsync(db, id, ct);
        if (p.Faction == req.Faction)                       // idempoten
            return Results.Ok(FactionState(p, rm));

        if (p.Faction != 0)                                  // sudah pilih → ini PINDAH
        {
            if (!SwitchAvailable(p, rm))
                return Results.Conflict(new
                {
                    error = p.FactionSwitchUsed
                        ? "Pindah faksi gratis sudah dipakai — faksi terkunci."
                        : "Placement ranked sudah selesai — faksi terkunci.",
                    faction = p.Faction,
                });
            p.FactionSwitchUsed = true;                      // jatah gratis terpakai
        }

        p.Faction = req.Faction;
        await db.SaveChangesAsync(ct);
        factions.Invalidate();                               // populasi berubah → hitung ulang "Butuh Pejuang"
        return Results.Ok(FactionState(p, rm));
    }

    private static Task<int> RankedMatchesAsync(AppDbContext db, Guid id, CancellationToken ct) =>
        db.MatchPlayers.CountAsync(mp => mp.PlayerId == id && mp.Match.Mode == "ranked" && mp.Match.Status == MatchStatus.Finished, ct);

    private static bool SwitchAvailable(Player p, int rankedMatches) =>
        !p.FactionSwitchUsed && rankedMatches < LeaderboardService.PlacementMatches;

    private static object FactionState(Player p, int rm) => new
    {
        faction = p.Faction,
        switchAvailable = p.Faction != 0 && SwitchAvailable(p, rm),
        locked = p.Faction != 0 && !SwitchAvailable(p, rm),
    };

    // Status populasi faksi + faksi yang "Butuh Pejuang" (minoritas) + bonus. Server-authoritative.
    private static async Task<IResult> GetFactions(FactionService factions, CancellationToken ct)
    {
        var s = await factions.GetAsync(ct);
        return Results.Ok(new { orde = s.Orde, legion = s.Legion, needsWarriors = s.NeedsWarriors, bonusPercent = s.BonusPercent });
    }

    // Gerbang admin: header X-Admin-Secret harus cocok Admin:Secret (kosong = selalu tolak → admin nonaktif).
    internal static async ValueTask<object?> RequireAdminSecret(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var expected = ctx.HttpContext.RequestServices.GetRequiredService<IOptions<AdminOptions>>().Value.Secret;
        var given = ctx.HttpContext.Request.Headers["X-Admin-Secret"].ToString();
        if (!PhotonEndpoints.SecretsEqual(expected, given))
            return Results.Json(new { error = "admin unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
        return await next(ctx);
    }

    // Admin/dev: set/reset faksi pemain (LEWATI kunci season). faction 0 = reset (pemain diminta pilih lagi).
    // Setiap perubahan WAJIB tercatat (FactionAdminAudit: sumber, lama→baru, waktu). UTANG RILIS: admin berbasis role (docs/90/91).
    private static async Task<IResult> AdminSetFaction(
        AdminFactionRequest req, HttpContext http, AppDbContext db, FactionService factions, ILoggerFactory loggers, CancellationToken ct)
    {
        if (req.Faction is < 0 or > 2) return Results.BadRequest(new { error = "faction 0 (reset), 1 (ORDE), atau 2 (LEGION)." });
        var p = await db.Players.FirstOrDefaultAsync(x => x.Id == req.PlayerId, ct);
        if (p is null) return Results.NotFound();

        var old = p.Faction;
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var label = string.IsNullOrWhiteSpace(req.AdminSource) ? "shared-secret" : req.AdminSource!.Trim();
        var source = $"{label}@{ip}";
        if (source.Length > 160) source = source[..160];

        p.Faction = req.Faction;
        p.FactionSwitchUsed = false;                         // reset admin = jatah pindah gratis dikembalikan
        db.FactionAdminAudits.Add(new FactionAdminAudit
        {
            PlayerId = p.Id, OldFaction = old, NewFaction = req.Faction, AdminSource = source,
        });
        await db.SaveChangesAsync(ct);
        factions.Invalidate();

        loggers.CreateLogger("AdminFaction").LogWarning(
            "Admin faction change: player {PlayerId} {Old}->{New} by {Source}", p.Id, old, req.Faction, source);
        return Results.Ok(new { playerId = p.Id, faction = p.Faction, previous = old });
    }

    private static object Serialize(MatchmakingResult r) => r.Assignment is { } a
        ? new { state = r.State.ToString(), gameId = a.GameId, slot = a.Slot, region = a.Region, opponentId = a.OpponentId, message = (string?)null }
        : new { state = r.State.ToString(), gameId = (string?)null, slot = (int?)null, region = (string?)null, opponentId = (Guid?)null, message = (string?)null };

    private static async Task<IResult> JoinQueue(
        JoinQueueRequest req, ClaimsPrincipal user, AppDbContext db, MatchmakingService mm,
        IOptions<GameOptions> game, CancellationToken ct)
    {
        var region = req.Region?.Trim() ?? "";
        if (region.Length is < 1 or > 32)
            return Results.BadRequest(new { error = "region must be 1-32 characters." });

        // Pengecekan versi: tolak build/sim berbeda agar tak match lalu desync. Hanya aktif bila dikonfigurasi.
        var g = game.Value;
        var sim = req.SimVersion ?? "";
        var build = req.BuildVersion ?? "";
        bool simBad = !string.IsNullOrEmpty(g.RequiredSimVersion) && sim != g.RequiredSimVersion;
        bool buildBad = !string.IsNullOrEmpty(g.RequiredBuildVersion) && build != g.RequiredBuildVersion;
        if (simBad || buildBad)
            return Results.Ok(new { state = "VersionMismatch", message = "Versi game berbeda, perbarui game.", gameId = (string?)null, slot = (int?)null, region = (string?)null, opponentId = (Guid?)null });

        var id = PlayerId(user);
        var p = await db.Players.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Results.NotFound();

        return Results.Ok(Serialize(mm.Join(id, p.Rating, region, build, sim)));
    }

    private static IResult QueueStatus(ClaimsPrincipal user, MatchmakingService mm) =>
        Results.Ok(Serialize(mm.Status(PlayerId(user))));

    private static IResult LeaveQueue(ClaimsPrincipal user, MatchmakingService mm)
    {
        mm.Leave(PlayerId(user));
        return Results.Ok(new { state = MatchmakingState.Idle.ToString() });
    }

    private static Guid PlayerId(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static async Task<IResult> GuestLogin(
        GuestLoginRequest req, AppDbContext db, TokenService tokens, IOptions<GameOptions> game, CancellationToken ct)
    {
        var deviceId = req.DeviceId?.Trim() ?? "";
        var name = req.DisplayName?.Trim() ?? "";
        if (deviceId.Length is < 8 or > 128)
            return Results.BadRequest(new { error = "deviceId must be 8-128 characters." });
        if (name.Length is < 1 or > 24)
            return Results.BadRequest(new { error = "displayName must be 1-24 characters." });

        var player = await db.Players.FirstOrDefaultAsync(p => p.DeviceId == deviceId, ct);
        if (player is null)
        {
            player = new Player
            {
                DeviceId = deviceId, DisplayName = name,
                Rating = game.Value.StartRating, Rd = game.Value.StartRd, Volatility = game.Value.StartVolatility,
                IsTestAccount = game.Value.AllowTestAccounts && (req.IsTest ?? false), // eksplisit; default false
            };
            db.Players.Add(player);
        }
        else
        {
            player.DisplayName = name;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two first-time logins for the same device raced; the winner's row is the one to use.
            db.Entry(player).State = EntityState.Detached;
            player = await db.Players.FirstAsync(p => p.DeviceId == deviceId, ct);
        }

        return Results.Ok(new
        {
            playerId = player.Id,
            displayName = player.DisplayName,
            rating = player.Rating,
            token = tokens.Create(player.Id, player.DisplayName),
        });
    }

    private static async Task<IResult> Me(ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var id = PlayerId(user);
        var p = await db.Players.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Results.NotFound();

        int matches = p.Wins + p.Losses;
        double winRate = matches > 0 ? Math.Round(100.0 * p.Wins / matches, 1) : 0.0;
        double kda = Math.Round((p.Kills + p.Assists) / (double)Math.Max(1, p.Deaths), 2);
        int level = Ranks.LevelFor(p.AccountXp);
        return Results.Ok(new
        {
            playerId = p.Id,
            username = p.Username,
            displayName = p.DisplayName,
            faction = p.Faction,
            rating = p.Rating,
            // ladder Solo 1v1 — dengan proteksi demosi (docs/33 opsi 1): tier tampil di-floor setelah naik tier.
            rankTier = Ranks.TierName(Ranks.ApplyFloor(
                Ranks.TierOrder(Ranks.TierFor(p.Rating)), p.TierFloorOrder, p.TierFloorLeft)),
            accountLevel = level,
            accountRank = Ranks.AccountRankFor(level), // Trainee→Colonel
            accountXp = p.AccountXp,
            wins = p.Wins,
            losses = p.Losses,
            matchesPlayed = matches,
            winRate,
            kills = p.Kills,
            deaths = p.Deaths,
            assists = p.Assists,
            kda,
            createdAt = p.CreatedAt,
        });
    }

    private static async Task<IResult> MyMatches(ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var id = PlayerId(user);
        var rows = await db.Matches.AsNoTracking()
            .Where(m => m.Status == MatchStatus.Finished && m.Players.Any(mp => mp.PlayerId == id))
            .OrderByDescending(m => m.EndedAt)
            .Take(20)
            .Select(m => new
            {
                gameId = m.GameId,
                endedAt = m.EndedAt,
                mode = m.Mode,
                format = m.Format,
                durationSec = m.DurationSec,
                scoreLeft = m.ScoreLeft,
                scoreRight = m.ScoreRight,
                endReason = m.EndReason,
                isDraw = m.IsDraw,
                koTimeline = m.KoTimeline,   // JSON mentah [{Tick,KillerSlot}] atau null (match lama)
                slot = m.Players.First(mp => mp.PlayerId == id).Slot,
                won = m.Players.First(mp => mp.PlayerId == id).IsWinner,
                ratingDelta = m.Players.First(mp => mp.PlayerId == id).RatingDelta,
                ratingBefore = m.Players.First(mp => mp.PlayerId == id).RatingBefore,
                heroId = m.Players.First(mp => mp.PlayerId == id).HeroId,
                partySize = m.Players.First(mp => mp.PlayerId == id).PartySize,
                hasDetailedStats = m.Players.First(mp => mp.PlayerId == id).HasDetailedStats,
                players = m.Players.OrderBy(mp => mp.Slot).Select(mp => new
                {
                    slot = mp.Slot,
                    displayName = mp.Player.DisplayName,
                    heroId = mp.HeroId,
                    kills = mp.ReportedKills,
                    deaths = mp.ReportedDeaths,
                    assists = mp.ReportedAssists,
                    ratingDelta = mp.RatingDelta,
                    ratingBefore = mp.RatingBefore,
                    isWinner = mp.IsWinner,
                    hasDetailedStats = mp.HasDetailedStats,
                    botKills = mp.BotKills,
                    damageDealt = mp.DamageDealt,
                    damageTaken = mp.DamageTaken,
                    skillsUsed = mp.SkillsUsed,
                    skillsHit = mp.SkillsHit,
                    hitsBlocked = mp.HitsBlocked,
                    dodges = mp.Dodges,
                    guardCrushes = mp.GuardCrushes,
                    criticals = mp.Criticals,
                    gearDrops = mp.GearDrops,
                    firstBlood = mp.FirstBlood,
                    maxKillStreak = mp.MaxKillStreak,
                    maxDeficit = mp.MaxDeficit,
                    heroSwitches = mp.HeroSwitches,
                    heroes = mp.Heroes.OrderBy(h => h.HeroId).Select(h => new
                    {
                        heroId = h.HeroId, playTicks = h.PlayTicks, kills = h.Kills, damage = h.Damage,
                    }).ToList(),
                }).ToList(),
            })
            .ToListAsync(ct);
        return Results.Ok(rows);
    }

    private static async Task<IResult> Leaderboard(LeaderboardService lb, int? limit, CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? 100, 1, 100);
        var all = await lb.GetAsync(ct);
        var rows = all.Take(take).Select(e => new
        {
            rank = e.Rank, displayName = e.DisplayName, tier = e.Tier, ladderPoints = e.LadderPoints,
            wins = e.Wins, losses = e.Losses, winRate = e.WinRate, matches = e.Matches,
        });
        return Results.Ok(rows);
    }

    // Peringkat pemain sendiri (untuk baris highlight / status placement di leaderboard).
    private static async Task<IResult> MyRank(ClaimsPrincipal user, LeaderboardService lb, AppDbContext db, CancellationToken ct)
    {
        var id = PlayerId(user);
        var e = await lb.RankOfAsync(id, ct);
        if (e != null)
            return Results.Ok(new { placed = true, rank = e.Rank, tier = e.Tier, ladderPoints = e.LadderPoints,
                wins = e.Wins, losses = e.Losses, winRate = e.WinRate, matches = e.Matches });
        int rm = await db.MatchPlayers.CountAsync(mp => mp.PlayerId == id && mp.Match.Mode == "ranked" && mp.Match.Status == MatchStatus.Finished, ct);
        return Results.Ok(new { placed = false, needed = Math.Max(0, LeaderboardService.PlacementMatches - rm), rankedMatches = rm });
    }

    private static async Task<IResult> ReportResult(
        ReportResultRequest req, ClaimsPrincipal user, MatchService matches, CancellationToken ct)
    {
        var gameId = req.GameId?.Trim() ?? "";
        if (gameId.Length is < 1 or > 128)
            return Results.BadRequest(new { error = "gameId must be 1-128 characters." });

        var outcome = await matches.ReportAsync(gameId, PlayerId(user), req.Slot, req.ScoreLeft, req.ScoreRight,
                                                req.Kills, req.Deaths, req.Assists,
                                                req.HeroId, req.Mode ?? "", req.DurationSec, req.PartySize, req.Players, ct,
                                                req.EndReason, req.WinnerSlot, req.KoTimeline);
        var body = new { status = outcome.Status.ToString(), message = outcome.Message };
        return outcome.Status switch
        {
            ReportStatus.Invalid => Results.BadRequest(body),
            ReportStatus.SlotTaken or ReportStatus.AlreadyFinal => Results.Conflict(body),
            _ => Results.Ok(body),
        };
    }
}
