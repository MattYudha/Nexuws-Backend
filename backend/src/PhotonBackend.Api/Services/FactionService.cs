using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PhotonBackend.Api.Data;

namespace PhotonBackend.Api.Services;

/// <summary>Status populasi faksi (server-authoritative). 0 = tak ada, 1 = ORDE, 2 = LEGION.</summary>
public sealed record FactionStatus(int Orde, int Legion, int NeedsWarriors, int BonusPercent);

/// <summary>
/// Keseimbangan faksi ORDE/LEGION (docs/35). Hitung populasi (akun non-uji yang sudah memilih),
/// tentukan faksi minoritas yang "Butuh Pejuang" + persen bonus. Dihitung di backend + cache 1 menit.
/// Dipakai endpoint /api/factions (UI onboarding) DAN MatchService (bonus XP faksi kecil).
/// </summary>
public sealed class FactionService(IServiceScopeFactory scopes, IOptions<GameOptions> game)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private FactionStatus _cache = new(0, 0, 0, 0);
    private DateTime _at = DateTime.MinValue;

    public async Task<FactionStatus> GetAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - _at < Ttl) return _cache;
        await _lock.WaitAsync(ct);
        try
        {
            if (DateTime.UtcNow - _at < Ttl) return _cache;
            _cache = await RecomputeAsync(ct);
            _at = DateTime.UtcNow;
            return _cache;
        }
        finally { _lock.Release(); }
    }

    /// <summary>Paksa recompute pada panggilan berikutnya (mis. setelah seseorang memilih faksi).</summary>
    public void Invalidate() => _at = DateTime.MinValue;

    private async Task<FactionStatus> RecomputeAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        int orde = await db.Players.CountAsync(p => !p.IsTestAccount && p.Faction == 1, ct);
        int legion = await db.Players.CountAsync(p => !p.IsTestAccount && p.Faction == 2, ct);

        int total = orde + legion;
        int needs = 0, bonus = 0;
        if (total >= game.Value.FactionMinPopulation && orde != legion)
        {
            int minority = orde < legion ? 1 : 2;
            double share = (double)Math.Min(orde, legion) / total;
            if (share < game.Value.FactionImbalanceThreshold)
            {
                needs = minority;
                bonus = game.Value.FactionJoinBonusPercent;
            }
        }
        return new FactionStatus(orde, legion, needs, bonus);
    }
}
