using Microsoft.Extensions.Options;
using PhotonBackend.Api;
using PhotonBackend.Api.Services;

namespace PhotonBackend.Tests;

// Q81 (kunci per username) & Q83 (kuota global guest baru).
public class AuthGuardTests
{
    private static (AuthGuard g, Func<DateTime> set, Action<double> advance) New(int maxFail = 3, int lockMin = 15, int guestPerMin = 2)
    {
        var now = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
        var g = new AuthGuard(Options.Create(new GameOptions { LoginMaxFailures = maxFail, LoginLockMinutes = lockMin, GuestCreatePerMinuteGlobal = guestPerMin }))
        { Clock = () => now };
        return (g, () => now, mins => now = now.AddMinutes(mins));
    }

    [Fact]
    public void Username_LockedAfterFailures_CaseInsensitive_ThenExpires()
    {
        var (g, _, advance) = New();
        for (int i = 0; i < 3; i++) g.RecordFailure("Budi");
        Assert.True(g.IsLocked("budi"));
        Assert.False(g.IsLocked("ani"));
        advance(16);
        Assert.False(g.IsLocked("BUDI"));
    }

    [Fact]
    public void SuccessfulLogin_ClearsFailures()
    {
        var (g, _, _) = New();
        g.RecordFailure("rio"); g.RecordFailure("rio");
        g.RecordSuccess("rio");
        g.RecordFailure("rio");
        Assert.False(g.IsLocked("rio"));
    }

    [Fact]
    public void GuestCreation_GlobalQuotaPerMinute()
    {
        var (g, _, advance) = New(guestPerMin: 2);
        Assert.True(g.TryCreateGuest()); Assert.True(g.TryCreateGuest());
        Assert.False(g.TryCreateGuest());
        advance(1.01);
        Assert.True(g.TryCreateGuest());
    }
}
