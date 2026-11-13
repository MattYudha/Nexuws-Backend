using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotonBackend.Api.Data;
using PhotonBackend.Api.Services;
using Xunit;

namespace PhotonBackend.Tests;

/// <summary>
/// KONSISTENSI TIER (docs/33 §2): tier yang DITAMPILKAN di leaderboard = tier dari RATING ASLI
/// (sama dengan profil/lobby/riwayat). Urutan posisi: TIER TAMPIL dulu (turun), lalu conservative rating
/// (rating − 2·RD) DI DALAM tier, tiebreak WR → match (T3).
/// </summary>
public class LeaderboardServiceTests
{
    // Scope factory minimal: selalu kembalikan SATU AppDbContext yang sama (deterministik, tanpa ambiguitas store).
    private sealed class OneContext(AppDbContext db) : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        public IServiceScope CreateScope() => this;
        public IServiceProvider ServiceProvider => this;
        public object? GetService(Type t) => t == typeof(AppDbContext) ? db : null;
        public void Dispose() { } // jangan dispose context yang dipakai ulang
    }

    private static (LeaderboardService svc, AppDbContext db) New()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("lb-" + Guid.NewGuid()).Options;
        var db = new AppDbContext(opts);
        return (new LeaderboardService(new OneContext(db)), db);
    }

    // Satu pemain yang SUDAH placement (5 ranked Finished) dengan rating & RD tertentu.
    private static void SeedPlaced(AppDbContext db, string name, int rating, double rd, int wins = 3, int losses = 2)
    {
        var p = new Player { DisplayName = name, Rating = rating, Rd = rd, Wins = wins, Losses = losses };
        db.Players.Add(p);
        for (int i = 0; i < 5; i++)
            db.Matches.Add(new Match
            {
                GameId = $"{name}-{i}", Mode = "ranked", Format = "ranked1v1", Status = MatchStatus.Finished,
                EndedAt = DateTime.UtcNow.AddMinutes(-i),
                Players = { new MatchPlayer { PlayerId = p.Id, Slot = 0 } },
            });
    }

    [Fact]
    public async Task Tier_FromRealRating_NotConservative()
    {
        var (svc, db) = New();
        // rating 1500 = Rival; conservative = 1500 − 2·350 = 800 = Rookie. Harus tampil RIVAL.
        SeedPlaced(db, "Highvar", rating: 1500, rd: 350);
        await db.SaveChangesAsync();

        var board = await svc.GetAsync(default);
        var e = Assert.Single(board);
        Assert.Equal("Rival", e.Tier);                        // dari rating asli, BUKAN "Rookie" (conservative)
        Assert.Equal(Ranks.TierFor(1500), e.Tier);            // konsisten dgn profil
        Assert.Equal(Ranks.LadderPoints(1500), e.LadderPoints);
    }

    [Fact]
    public async Task Position_OrderedByConservativeRating()
    {
        var (svc, db) = New();
        // A: rating lebih RENDAH tapi RD kecil → conservative TINGGI (1400).
        SeedPlaced(db, "Steady", rating: 1500, rd: 50);    // cons = 1400
        // B: rating lebih TINGGI tapi RD besar → conservative RENDAH (850).
        SeedPlaced(db, "Volatile", rating: 1550, rd: 350); // cons = 850
        await db.SaveChangesAsync();

        var board = await svc.GetAsync(default);
        Assert.Equal(2, board.Count);
        Assert.Equal("Steady", board[0].DisplayName);   // cons lebih tinggi → posisi 1
        Assert.Equal("Volatile", board[1].DisplayName);
        Assert.Equal(1, board[0].Rank);
        Assert.Equal(2, board[1].Rank);
        // Namun tier keduanya tetap dari rating asli (sama-sama Rival).
        Assert.Equal("Rival", board[0].Tier);
        Assert.Equal("Rival", board[1].Tier);
    }

    [Fact]
    public async Task Position_TierFirst_HighRdGladiatorNotBelowRival()
    {
        var (svc, db) = New();
        // Gladiator RD tinggi: cons = 1650 − 2·300 = 1050. Rival stabil: cons = 1550 − 2·40 = 1470.
        // Urut conservative murni → Rival di atas Gladiator (tier terbalik di papan). T3: TIER DULU.
        SeedPlaced(db, "WildGlad", rating: 1650, rd: 300);
        SeedPlaced(db, "SteadyRival", rating: 1550, rd: 40);
        await db.SaveChangesAsync();

        var board = await svc.GetAsync(default);
        Assert.Equal("WildGlad", board[0].DisplayName);
        Assert.Equal("Gladiator", board[0].Tier);
        Assert.Equal("SteadyRival", board[1].DisplayName);
        Assert.Equal("Rival", board[1].Tier);
    }

    // Q71: kolom Matches = jumlah ranked selesai (seri ikut) = basis placement, bukan W+L.
    [Fact]
    public async Task Matches_CountsRankedFinished_IncludingDraws()
    {
        var (svc, db) = New();
        SeedPlaced(db, "AllDraws", rating: 1500, rd: 80, wins: 0, losses: 0); // 5 ranked selesai, semuanya seri
        await db.SaveChangesAsync();

        var e = Assert.Single(await svc.GetAsync(default));
        Assert.Equal(5, e.Matches);
        Assert.Equal(0, e.Wins); Assert.Equal(0, e.Losses);
    }
}
