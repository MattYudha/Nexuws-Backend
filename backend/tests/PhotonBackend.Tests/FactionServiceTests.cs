using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PhotonBackend.Api;
using PhotonBackend.Api.Data;
using PhotonBackend.Api.Services;
using Xunit;

namespace PhotonBackend.Tests;

/// <summary>Keseimbangan faksi (docs/35): minoritas "Butuh Pejuang" + bonus; akun uji dikecualikan.</summary>
public class FactionServiceTests
{
    private sealed class OneContext(AppDbContext db) : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        public IServiceScope CreateScope() => this;
        public IServiceProvider ServiceProvider => this;
        public object? GetService(Type t) => t == typeof(AppDbContext) ? db : null;
        public void Dispose() { }
    }

    private static (FactionService svc, AppDbContext db) New(GameOptions? opts = null)
    {
        var o = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("fac-" + Guid.NewGuid()).Options;
        var db = new AppDbContext(o);
        var svc = new FactionService(new OneContext(db), Options.Create(opts ?? new GameOptions { FactionMinPopulation = 2 }));
        return (svc, db);
    }

    private static void Add(AppDbContext db, int faction, int n, bool test = false)
    {
        for (int i = 0; i < n; i++)
            db.Players.Add(new Player { DisplayName = $"f{faction}-{Guid.NewGuid():N}", Faction = faction, IsTestAccount = test });
    }

    [Fact]
    public async Task Balanced_NoNeedsWarriors()
    {
        var (svc, db) = New();
        Add(db, 1, 3); Add(db, 2, 3);
        await db.SaveChangesAsync();

        var s = await svc.GetAsync(default);
        Assert.Equal(3, s.Orde);
        Assert.Equal(3, s.Legion);
        Assert.Equal(0, s.NeedsWarriors);
        Assert.Equal(0, s.BonusPercent);
    }

    [Fact]
    public async Task Imbalanced_MinorityNeedsWarriors_WithBonus()
    {
        var (svc, db) = New(new GameOptions { FactionMinPopulation = 2, FactionImbalanceThreshold = 0.45, FactionJoinBonusPercent = 15 });
        Add(db, 1, 1); Add(db, 2, 5);   // ORDE minoritas (1/6 ≈ 0.17 < 0.45)
        await db.SaveChangesAsync();

        var s = await svc.GetAsync(default);
        Assert.Equal(1, s.NeedsWarriors);     // ORDE
        Assert.Equal(15, s.BonusPercent);
    }

    [Fact]
    public async Task BelowMinPopulation_NoBonus()
    {
        var (svc, db) = New(new GameOptions { FactionMinPopulation = 10 });
        Add(db, 1, 1); Add(db, 2, 3);   // total 4 < 10
        await db.SaveChangesAsync();

        var s = await svc.GetAsync(default);
        Assert.Equal(0, s.NeedsWarriors);
    }

    [Fact]
    public async Task TestAccounts_Excluded()
    {
        var (svc, db) = New();
        Add(db, 1, 2); Add(db, 2, 2, test: true);   // LEGION semuanya akun uji → tak dihitung
        await db.SaveChangesAsync();

        var s = await svc.GetAsync(default);
        Assert.Equal(2, s.Orde);
        Assert.Equal(0, s.Legion);
    }
}
