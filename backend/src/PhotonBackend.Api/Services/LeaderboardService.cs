using Microsoft.EntityFrameworkCore;
using PhotonBackend.Api.Data;

namespace PhotonBackend.Api.Services;

public sealed record LeaderEntry(int Rank, Guid PlayerId, string DisplayName, string Tier, int LadderPoints,
    int Wins, int Losses, double WinRate, int Matches);

/// <summary>
/// Leaderboard ranked yang bisa dipahami (docs/33): hanya akun non-uji yang SELESAI placement (≥5 ranked),
/// diurutkan tier → Poin Ladder → win rate → jumlah match, posisi dari CONSERVATIVE rating (rating − 2·RD)
/// supaya akun ketidakpastian tinggi tak melompat ke puncak. Dihitung di backend + cache 1 menit.
/// </summary>
public sealed class LeaderboardService(IServiceScopeFactory scopes)
{
    public const int PlacementMatches = 5;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<LeaderEntry> _cache = new();
    private Dictionary<Guid, LeaderEntry> _byId = new();
    private DateTime _at = DateTime.MinValue;

    public async Task<IReadOnlyList<LeaderEntry>> GetAsync(CancellationToken ct)
    {
        await EnsureFreshAsync(ct);
        return _cache;
    }

    public async Task<LeaderEntry?> RankOfAsync(Guid playerId, CancellationToken ct)
    {
        await EnsureFreshAsync(ct);
        return _byId.TryGetValue(playerId, out var e) ? e : null;
    }

    private async Task EnsureFreshAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - _at < Ttl) return;
        await _lock.WaitAsync(ct);
        try
        {
            if (DateTime.UtcNow - _at < Ttl) return;
            await RecomputeAsync(ct);
            _at = DateTime.UtcNow;
        }
        finally { _lock.Release(); }
    }

    private async Task RecomputeAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rankedCount = await db.MatchPlayers.AsNoTracking()
            .Where(mp => mp.Match.Mode == "ranked" && mp.Match.Status == MatchStatus.Finished)
            .GroupBy(mp => mp.PlayerId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var players = await db.Players.AsNoTracking()
            .Where(p => !p.IsTestAccount)
            .Select(p => new { p.Id, p.DisplayName, p.Rating, p.Rd, p.Wins, p.Losses, p.TierFloorOrder, p.TierFloorLeft })
            .ToListAsync(ct);

        var rows = new List<(string Tier, int Pts, double Wr, int Matches, Guid Id, string Name, int W, int L)>();
        foreach (var p in players)
        {
            int rm = rankedCount.TryGetValue(p.Id, out var c) ? c : 0;
            if (rm < PlacementMatches) continue; // belum selesai placement

            int cons = (int)Math.Round(p.Rating - 2 * p.Rd); // conservative rating → posisi
            int baseOrder = Ranks.TierOrder(Ranks.TierFor(cons));
            int dispOrder = Ranks.ApplyFloor(baseOrder, p.TierFloorOrder, p.TierFloorLeft); // proteksi demosi (opsi 1)
            string tier = Ranks.TierName(dispOrder);
            int pts = dispOrder > baseOrder ? 0 : Ranks.LadderPoints(cons); // di-floor → dasar tier baru (0 poin)
            int total = p.Wins + p.Losses;
            double wr = total > 0 ? Math.Round(100.0 * p.Wins / total, 1) : 0;
            rows.Add((tier, pts, wr, total, p.Id, p.DisplayName, p.Wins, p.Losses));
        }

        rows.Sort((a, b) =>
        {
            int t = Ranks.TierOrder(b.Tier).CompareTo(Ranks.TierOrder(a.Tier)); if (t != 0) return t;
            int pt = b.Pts.CompareTo(a.Pts); if (pt != 0) return pt;
            int wr = b.Wr.CompareTo(a.Wr); if (wr != 0) return wr;
            return b.Matches.CompareTo(a.Matches);
        });

        var list = new List<LeaderEntry>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            list.Add(new LeaderEntry(i + 1, r.Id, r.Name, r.Tier, r.Pts, r.W, r.L, r.Wr, r.Matches));
        }
        _cache = list;
        _byId = list.ToDictionary(x => x.PlayerId);
    }
}
