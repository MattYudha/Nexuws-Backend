using Microsoft.Extensions.Options;

namespace PhotonBackend.Api.Services;

public enum MatchmakingState { Idle, Searching, Matched }

public sealed record MatchAssignment(string GameId, int Slot, string Region, Guid OpponentId);
public sealed record MatchmakingResult(MatchmakingState State, MatchAssignment? Assignment);

/// <summary>
/// Antrean matchmaking MMR untuk Solo 1v1 (in-memory, singleton). Memasangkan pemain dengan
/// rating terdekat dalam <see cref="GameOptions.MatchmakingWindow"/> pada region yang sama,
/// lalu memberi <c>gameId</c> (nama room Photon) bersama + slot (0/1). Match row dibuat lazy
/// saat hasil dilapor (<see cref="MatchService"/>), jadi service ini tak butuh DB.
///
/// Catatan skala: state in-process → valid untuk satu instance API. Beberapa replika butuh
/// store bersama (Redis) — lihat MatchService untuk pola lock yang sama.
/// </summary>
public sealed class MatchmakingService(IOptions<GameOptions> options)
{
    // LastSeen = poll terakhir (Join/Status). Penunggu yang berhenti polling (klien crash/tutup) dibuang.
    private sealed record Waiter(int Rating, string Region, DateTime At, string Build, string Sim, DateTime LastSeen);

    private readonly object _lock = new();
    private readonly Dictionary<Guid, Waiter> _waiting = new();
    private readonly Dictionary<Guid, (MatchAssignment A, DateTime At)> _assignments = new();
    private readonly GameOptions _o = options.Value;

    /// <summary>Jam (UTC). Bisa diganti di tes.</summary>
    public Func<DateTime> Clock { get; init; } = () => DateTime.UtcNow;

    // Buang penunggu basi (tak polling) + assignment kedaluwarsa (klien crash sebelum /leave). Dipanggil di dalam _lock.
    private void Prune(DateTime now)
    {
        foreach (var id in _waiting.Where(kv => (now - kv.Value.LastSeen).TotalSeconds > _o.MatchmakingWaiterStaleSeconds).Select(kv => kv.Key).ToList())
            _waiting.Remove(id);
        foreach (var id in _assignments.Where(kv => (now - kv.Value.At).TotalSeconds > _o.MatchmakingAssignmentTtlSeconds).Select(kv => kv.Key).ToList())
            _assignments.Remove(id);
    }

    public MatchmakingResult Join(Guid playerId, int rating, string region, string build = "", string sim = "")
    {
        lock (_lock)
        {
            var now = Clock();
            Prune(now);
            if (_assignments.TryGetValue(playerId, out var already))
                return new MatchmakingResult(MatchmakingState.Matched, already.A);

            // Upsert: PERTAHANKAN waktu masuk antre lama agar pelebaran rentang terus berjalan.
            var at = _waiting.TryGetValue(playerId, out var ex) ? ex.At : now;
            _waiting[playerId] = new Waiter(rating, region, at, build ?? "", sim ?? "", now);
            return TryMatch(playerId, now);
        }
    }

    public MatchmakingResult Status(Guid playerId)
    {
        lock (_lock)
        {
            var now = Clock();
            Prune(now);
            if (_assignments.TryGetValue(playerId, out var a)) return new MatchmakingResult(MatchmakingState.Matched, a.A);
            if (_waiting.TryGetValue(playerId, out var w))
            {
                _waiting[playerId] = w with { LastSeen = now };   // masih polling → masih hidup
                return TryMatch(playerId, now);                    // re-evaluasi: rentang mungkin sudah melebar
            }
            return new MatchmakingResult(MatchmakingState.Idle, null);
        }
    }

    // Dipanggil di dalam _lock; playerId dijamin ada di _waiting (atau sudah ter-assign).
    private MatchmakingResult TryMatch(Guid playerId, DateTime now)
    {
        if (_assignments.TryGetValue(playerId, out var already))
            return new MatchmakingResult(MatchmakingState.Matched, already.A);

        var me = _waiting[playerId];
        int myWin = Window((now - me.At).TotalSeconds);

        // Lawan terbaik: region + VERSI sama, selisih terkecil dalam rentang efektif (maks dari kedua penunggu).
        Guid bestId = default; int bestDiff = int.MaxValue; bool found = false;
        foreach (var (oid, w) in _waiting)
        {
            if (oid == playerId) continue;
            if (!string.Equals(w.Region, me.Region, StringComparison.OrdinalIgnoreCase)) continue;
            if (w.Sim != me.Sim || w.Build != me.Build) continue; // jangan pasangkan versi berbeda
            int pairWin = Math.Max(myWin, Window((now - w.At).TotalSeconds));
            int diff = Math.Abs(w.Rating - me.Rating);
            if (diff <= pairWin && diff < bestDiff) { bestId = oid; bestDiff = diff; found = true; }
        }

        if (!found) return new MatchmakingResult(MatchmakingState.Searching, null);

        _waiting.Remove(bestId);
        _waiting.Remove(playerId);
        var gameId = "mm-" + Guid.NewGuid().ToString("N")[..12];
        _assignments[bestId] = (new MatchAssignment(gameId, 0, me.Region, playerId), now);   // lawan yang sudah ada di antrean = slot 0
        _assignments[playerId] = (new MatchAssignment(gameId, 1, me.Region, bestId), now);   // pemicu pasangan (Join/Status ini) = slot 1
        return new MatchmakingResult(MatchmakingState.Matched, _assignments[playerId].A);
    }

    // Rentang rating efektif: melebar per detik, lalu tak terbatas setelah MaxWait (selalu dapat lawan).
    private int Window(double elapsedSec)
    {
        if (_o.MatchmakingIgnoreRating) return int.MaxValue;                  // debug dev
        if (elapsedSec >= _o.MatchmakingMaxWaitSeconds) return int.MaxValue;  // batas tunggu → terima siapa pun
        return _o.MatchmakingWindowStart + (int)(_o.MatchmakingWindowGrowthPerSec * elapsedSec);
    }

    /// <summary>Keluar antre / lepas assignment (mis. sudah connect atau batal).</summary>
    public void Leave(Guid playerId)
    {
        lock (_lock)
        {
            _waiting.Remove(playerId);
            _assignments.Remove(playerId);
        }
    }
}
