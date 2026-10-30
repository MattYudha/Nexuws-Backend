using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PhotonBackend.Api.Data;
using PhotonBackend.Api.Services;

namespace PhotonBackend.Api.Endpoints;

/// <summary>
/// Statistik turunan (docs/32 Lapisan 2): agregat, rekor, flawless/comeback/close, dan RADAR 5 sumbu
/// (persentil terhadap pemain tier yang sama, minimal 10 match berdetail). Satu sumber = data per-match.
/// </summary>
public static class StatsEndpoints
{
    private const int RadarMinMatches = 10;   // pemain harus punya >=10 match berdetail agar radar tampil
    private const int RadarMinPopulation = 5; // minimal pembanding (termasuk pemain) agar persentil bermakna

    public static void MapStats(this IEndpointRouteBuilder secured, IEndpointRouteBuilder anon)
    {
        secured.MapGet("/me/stats", async (ClaimsPrincipal user, AppDbContext db, string? mode, CancellationToken ct)
            => Results.Ok(await Compute(db, PlayerId(user), mode, ct)));
        anon.MapGet("/players/{id:guid}/stats", async (Guid id, AppDbContext db, string? mode, CancellationToken ct)
            => Results.Ok(await Compute(db, id, mode, ct)));
    }

    private sealed record Row(
        DateTime? EndedAt, int ScoreLeft, int ScoreRight, int DurationSec, int Slot, bool Won,
        int Kills, int Deaths, bool Detailed, bool KdValid,
        int DamageDealt, int DamageTaken, int HitsBlocked, int Dodges, int GuardCrushes, int Criticals, int MaxDeficit, bool FirstBlood);

    private static async Task<object> Compute(AppDbContext db, Guid playerId, string? mode, CancellationToken ct)
    {
        mode = string.IsNullOrWhiteSpace(mode) ? null : mode.Trim().ToLowerInvariant();

        var rows = await RowsFor(db, playerId, mode, ct);
        var a = Aggregate(rows);

        var player = await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Id == playerId, ct);
        string tier = player != null ? Ranks.TierFor(player.Rating) : "Rookie";
        var radar = await BuildRadar(db, playerId, tier, mode, a, ct);

        return new
        {
            mode = mode ?? "all",
            matches = a.Matches,
            wins = a.Wins,
            losses = a.Losses,
            winRate = a.WinRate,
            kdValid = a.KdMatches > 0,   // false → UI tampilkan "–" untuk K/D (match lama 5v5 tak dihitung)
            kdMatches = a.KdMatches,
            kills = a.Kills,
            deaths = a.Deaths,
            kda = a.Kda,
            maxKill = a.MaxKill,
            winStreakBest = a.WinStreakBest,
            avgDurationSec = a.AvgDurationSec,
            hasDetailed = a.Detailed > 0,
            detailedMatches = a.Detailed,
            // rekor mekanik khas (hanya dari match berdetail)
            damageDealt = a.DamageDealt,
            damageTaken = a.DamageTaken,
            hitsBlocked = a.HitsBlocked,
            dodges = a.Dodges,
            guardCrushes = a.GuardCrushes,
            criticals = a.Criticals,
            firstBloods = a.FirstBloods,
            flawless = a.Flawless,
            comebacks = a.Comebacks,
            closeWins = a.CloseWins,
            closeMatches = a.CloseMatches,
            tier,
            radar,
        };
    }

    private static async Task<List<Row>> RowsFor(AppDbContext db, Guid playerId, string? mode, CancellationToken ct) =>
        await db.MatchPlayers.AsNoTracking()
            .Where(mp => mp.PlayerId == playerId && mp.Match.Status == MatchStatus.Finished
                         && (mode == null || mp.Match.Mode == mode))
            .OrderBy(mp => mp.Match.EndedAt)
            .Select(mp => new Row(
                mp.Match.EndedAt, mp.Match.ScoreLeft ?? 0, mp.Match.ScoreRight ?? 0, mp.Match.DurationSec,
                mp.Slot, mp.IsWinner, mp.ReportedKills, mp.ReportedDeaths, mp.HasDetailedStats, mp.Match.Format == "ranked1v1",
                mp.DamageDealt, mp.DamageTaken, mp.HitsBlocked, mp.Dodges, mp.GuardCrushes, mp.Criticals, mp.MaxDeficit, mp.FirstBlood))
            .ToListAsync(ct);

    private sealed class Agg
    {
        public int Matches, Wins, Losses, Kills, Deaths, MaxKill, WinStreakBest, AvgDurationSec;
        public int KdMatches; // match dgn K/D valid (format ranked1v1) → kalau 0, K/D tampil "–"
        public double WinRate, Kda;
        public int Detailed, DamageDealt, DamageTaken, HitsBlocked, Dodges, GuardCrushes, Criticals;
        public int FirstBloods, Flawless, Comebacks, CloseWins, CloseMatches;
        public double DetailedMinutes, DetailedKoReceived;
    }

    private static Agg Aggregate(List<Row> rows)
    {
        var a = new Agg { Matches = rows.Count };
        int durSum = 0, cur = 0;
        foreach (var r in rows)
        {
            if (r.Won) { a.Wins++; cur++; if (cur > a.WinStreakBest) a.WinStreakBest = cur; } else { a.Losses++; cur = 0; }
            if (r.KdValid) // K/D hanya dari format valid (ranked1v1); match lama 5v5 TIDAK dihitung
            {
                a.KdMatches++; a.Kills += r.Kills; a.Deaths += r.Deaths;
                if (r.Kills > a.MaxKill) a.MaxKill = r.Kills;
            }
            durSum += r.DurationSec;

            int diff = Math.Abs(r.ScoreLeft - r.ScoreRight);
            int loserScore = Math.Min(r.ScoreLeft, r.ScoreRight);
            if (diff <= 2) { a.CloseMatches++; if (r.Won) a.CloseWins++; }
            if (r.Won && loserScore == 0) a.Flawless++;

            if (r.Detailed)
            {
                a.Detailed++;
                a.DamageDealt += r.DamageDealt; a.DamageTaken += r.DamageTaken;
                a.HitsBlocked += r.HitsBlocked; a.Dodges += r.Dodges;
                a.GuardCrushes += r.GuardCrushes; a.Criticals += r.Criticals;
                a.DetailedMinutes += r.DurationSec / 60.0;
                a.DetailedKoReceived += r.Deaths;
                if (r.FirstBlood) a.FirstBloods++;
                if (r.Won && r.MaxDeficit >= 3) a.Comebacks++;
            }
        }
        a.WinRate = a.Matches > 0 ? Math.Round(100.0 * a.Wins / a.Matches, 1) : 0;
        a.Kda = Math.Round((a.Kills + 0.0) / Math.Max(1, a.Deaths), 2);
        a.AvgDurationSec = a.Matches > 0 ? durSum / a.Matches : 0;
        return a;
    }

    // Lima sumbu radar sebagai skalar (docs/32). Dipakai untuk persentil terhadap tier.
    private static double[] RadarAxes(Agg a)
    {
        double min = Math.Max(a.DetailedMinutes, 1e-6);
        double serangan = a.DamageDealt / min;
        double pertahanan = (a.HitsBlocked + a.Dodges) / min;
        double tekanan = (a.GuardCrushes + a.Criticals) / min;
        double ketahanan = a.DamageTaken / Math.Max(1.0, a.DetailedKoReceived);
        double closeWinRate = a.CloseMatches > 0 ? (double)a.CloseWins / a.CloseMatches : 0;
        double clutch = a.Comebacks + closeWinRate;
        return new[] { serangan, pertahanan, tekanan, ketahanan, clutch };
    }

    private static async Task<object> BuildRadar(AppDbContext db, Guid playerId, string tier, string? mode, Agg mineAgg, CancellationToken ct)
    {
        string[] labels = { "Serangan", "Pertahanan", "Tekanan", "Ketahanan", "Clutch" };

        if (mineAgg.Detailed < RadarMinMatches)
            return new { ready = false, reason = $"Radar muncul setelah {RadarMinMatches} pertandingan berdetail.", labels };

        // Kandidat tier sama (populasi kecil → muat semua pemain, filter tier di memori).
        var players = await db.Players.AsNoTracking().Select(p => new { p.Id, p.Rating }).ToListAsync(ct);
        var tierIds = players.Where(p => Ranks.TierFor(p.Rating) == tier).Select(p => p.Id).ToHashSet();
        tierIds.Add(playerId);

        // Populasi: tiap pemain tier (dengan >=10 match berdetail) → satu vektor 5 sumbu.
        var byPlayer = await db.MatchPlayers.AsNoTracking()
            .Where(mp => tierIds.Contains(mp.PlayerId) && mp.HasDetailedStats
                         && mp.Match.Status == MatchStatus.Finished && (mode == null || mp.Match.Mode == mode))
            .Select(mp => new
            {
                mp.PlayerId,
                Row = new Row(mp.Match.EndedAt, mp.Match.ScoreLeft ?? 0, mp.Match.ScoreRight ?? 0, mp.Match.DurationSec,
                    mp.Slot, mp.IsWinner, mp.ReportedKills, mp.ReportedDeaths, true, mp.Match.Format == "ranked1v1",
                    mp.DamageDealt, mp.DamageTaken, mp.HitsBlocked, mp.Dodges, mp.GuardCrushes, mp.Criticals, mp.MaxDeficit, mp.FirstBlood),
            })
            .ToListAsync(ct);

        var axisVectors = byPlayer
            .GroupBy(x => x.PlayerId)
            .Select(g => g.Select(x => x.Row).ToList())
            .Where(list => list.Count >= RadarMinMatches)
            .Select(list => RadarAxes(Aggregate(list)))
            .ToList();

        if (axisVectors.Count < RadarMinPopulation)
            return new { ready = false, reason = "Radar muncul setelah cukup pembanding di tier ini.", labels };

        var mineAxes = RadarAxes(mineAgg);
        var values = new double[5];
        for (int ax = 0; ax < 5; ax++)
        {
            int leq = axisVectors.Count(v => v[ax] <= mineAxes[ax]);
            values[ax] = Math.Round(100.0 * leq / axisVectors.Count, 0); // persentil 0..100
        }
        return new { ready = true, labels, values };
    }

    private static Guid PlayerId(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? user.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
