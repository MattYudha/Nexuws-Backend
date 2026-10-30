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
    private sealed record Waiter(int Rating, string Region, DateTime At, string Build, string Sim);

    private readonly object _lock = new();
    private readonly Dictionary<Guid, Waiter> _waiting = new();
    private readonly Dictionary<Guid, MatchAssignment> _assignments = new();
    private readonly GameOptions _o = options.Value;

    public MatchmakingResult Join(Guid playerId, int rating, string region, string build = "", string sim = "")
    {
        lock (_lock)
        {
            if (_assignments.TryGetValue(playerId, out var already))
                return new MatchmakingResult(MatchmakingState.Matched, already);

            // Upsert: PERTAHANKAN waktu masuk antre lama agar pelebaran rentang terus berjalan.
            var at = _waiting.TryGetValue(playerId, out var ex) ? ex.At : DateTime.UtcNow;
            _waiting[playerId] = new Waiter(rating, region, at, build ?? "", sim ?? "");
            return TryMatch(playerId);
        }
    }

    public MatchmakingResult Status(Guid playerId)
    {
        lock (_lock)
        {
            if (_assignments.TryGetValue(playerId, out var a)) return new MatchmakingResult(MatchmakingState.Matched, a);
            if (_waiting.ContainsKey(playerId)) return TryMatch(playerId); // re-evaluasi: rentang mungkin sudah melebar
            return new MatchmakingResult(MatchmakingState.Idle, null);
        }
    }

    // Dipanggil di dalam _lock; playerId dijamin ada di _waiting (atau sudah ter-assign).
    private MatchmakingResult TryMatch(Guid playerId)
    {
        if (_assignments.TryGetValue(playerId, out var already))
            return new MatchmakingResult(MatchmakingState.Matched, already);

        var me = _waiting[playerId];
        var now = DateTime.UtcNow;
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
        _assignments[bestId] = new MatchAssignment(gameId, 0, me.Region, playerId);   // yang menunggu lebih dulu = slot 0
        _assignments[playerId] = new MatchAssignment(gameId, 1, me.Region, bestId);   // pendatang = slot 1
        return new MatchmakingResult(MatchmakingState.Matched, _assignments[playerId]);
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
