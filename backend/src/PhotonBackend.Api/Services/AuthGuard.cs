using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace PhotonBackend.Api.Services;

/// <summary>
/// Pelengkap rate limit per IP (Q81/Q83). In-memory, satu instance (multi-replika → store bersama, NX-IN-10).
/// - Login gagal per USERNAME: brute force terdistribusi dari banyak IP tetap terkunci sementara.
/// - Pembuatan guest BARU secara global per menit: flood dari banyak IP tak bisa membanjiri DB.
/// </summary>
public sealed class AuthGuard(IOptions<GameOptions> options)
{
    private readonly GameOptions _o = options.Value;
    private readonly ConcurrentDictionary<string, (int Fails, DateTime Since)> _fails = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _guestLock = new();
    private DateTime _guestWindow = DateTime.MinValue;
    private int _guestCount;

    public Func<DateTime> Clock { get; init; } = () => DateTime.UtcNow;

    /// <summary>true bila username sedang dikunci karena terlalu banyak login gagal dalam jendela.</summary>
    public bool IsLocked(string username)
    {
        if (!_fails.TryGetValue(username, out var f)) return false;
        if ((Clock() - f.Since).TotalMinutes >= _o.LoginLockMinutes) { _fails.TryRemove(username, out _); return false; }
        return f.Fails >= _o.LoginMaxFailures;
    }

    public void RecordFailure(string username)
    {
        var now = Clock();
        _fails.AddOrUpdate(username, _ => (1, now),
            (_, f) => (now - f.Since).TotalMinutes >= _o.LoginLockMinutes ? (1, now) : (f.Fails + 1, f.Since));
    }

    public void RecordSuccess(string username) => _fails.TryRemove(username, out _);

    /// <summary>Ambil satu jatah pembuatan guest baru (global, per menit). false = kuota habis → 429.</summary>
    public bool TryCreateGuest()
    {
        lock (_guestLock)
        {
            var now = Clock();
            if ((now - _guestWindow).TotalMinutes >= 1) { _guestWindow = now; _guestCount = 0; }
            if (_guestCount >= _o.GuestCreatePerMinuteGlobal) return false;
            _guestCount++;
            return true;
        }
    }
}
