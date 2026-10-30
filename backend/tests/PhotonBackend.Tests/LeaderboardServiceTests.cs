using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotonBackend.Api.Data;
using PhotonBackend.Api.Services;
using Xunit;

namespace PhotonBackend.Tests;

/// <summary>
/// KONSISTENSI TIER (docs/33 §2): tier yang DITAMPILKAN di leaderboard = tier dari RATING ASLI
/// (sama dengan profil/lobby/riwayat). Conservative rating (rating − 2·RD) HANYA untuk URUTAN POSISI.
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
}
