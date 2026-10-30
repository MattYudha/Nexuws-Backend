using PhotonBackend.Api.Services;
using Xunit;

namespace PhotonBackend.Tests;

public class RanksTests
{
    // Backfill LAMA berbasis NAMA saja (bukan deviceId). Akun asli (nama biasa) tak tersentuh.
    [Theory]
    [InlineData(null, "Quade", false)]          // nama asli
    [InlineData(null, "E2E_A", true)]
    [InlineData(null, "FB_A", true)]
    [InlineData("logintest_1", "logintest_1", true)]
    [InlineData(null, "Juara", false)]          // login FB asli bernama biasa → BUKAN uji
    public void IsLegacyTestName_MatchesOldTestAccountsByName(string? user, string? name, bool expected)
        => Assert.Equal(expected, Ranks.IsLegacyTestName(user, name));

    [Theory]
    [InlineData(1500, 2)]   // Rival (1400–1600): (1500-1400)/200 = 50
    [InlineData(1400, 2)]   // batas bawah Rival = 0 poin
    [InlineData(1399, 1)]   // Contender
    [InlineData(2000, 5)]   // Nexus Legend
    public void TierOrder_FromRating(int rating, int expectedOrder)
        => Assert.Equal(expectedOrder, Ranks.TierOrder(Ranks.TierFor(rating)));

    [Theory]
    [InlineData(1500, 50)]  // tengah Rival
    [InlineData(1400, 0)]   // awal tier
    [InlineData(1550, 75)]  // 3/4 menuju Gladiator
    public void LadderPoints_ProgressWithinTier(int rating, int expected)
        => Assert.Equal(expected, Ranks.LadderPoints(rating));

    // --- PROTEKSI DEMOSI (docs/33 opsi 1): floor tier di layer tampilan, Glicko tetap murni ---

    [Theory]
    [InlineData(0, "Rookie")]
    [InlineData(2, "Rival")]
    [InlineData(5, "Nexus Legend")]
    public void TierName_IsInverseOfTierOrder(int order, string name)
    {
        Assert.Equal(name, Ranks.TierName(order));
        Assert.Equal(order, Ranks.TierOrder(name));
    }

    [Fact]
    public void ApplyFloor_RaisesWhileActive_IgnoredWhenExpired()
    {
        Assert.Equal(2, Ranks.ApplyFloor(baseOrder: 1, floorOrder: 2, floorLeft: 3)); // di-floor ke Rival
        Assert.Equal(3, Ranks.ApplyFloor(baseOrder: 3, floorOrder: 2, floorLeft: 3)); // base lebih tinggi → base
        Assert.Equal(1, Ranks.ApplyFloor(baseOrder: 1, floorOrder: 2, floorLeft: 0)); // proteksi habis → base
    }

    [Fact]
    public void StepTierFloor_PromotionSetsFloor_HoldsForGrace_ThenDrops()
    {
        const int grace = 3;
        // Mulai Contender (order 1), belum ada floor.
        int last = 1, floor = 0, left = 0;

        // Menang → naik ke Rival (1400+). PROMOSI: floor=Rival, grace penuh.
        (last, floor, left) = Ranks.StepTierFloor(last, 1450, floor, left, grace);
        Assert.Equal((2, 2, 3), (last, floor, left));
        Assert.Equal(2, Ranks.ApplyFloor(Ranks.TierOrder(Ranks.TierFor(1450)), floor, left)); // tampil Rival

        // Tiga kali turun ke Contender (1350) → tampil TETAP Rival selama grace.
        for (int i = 0; i < grace; i++)
        {
            (last, floor, left) = Ranks.StepTierFloor(last, 1350, floor, left, grace);
            int disp = Ranks.ApplyFloor(Ranks.TierOrder(Ranks.TierFor(1350)), floor, left);
            if (i < grace - 1) Assert.Equal(2, disp); // masih dilindungi → Rival
        }
        Assert.Equal(0, left); // grace habis

        // Match berikut masih Contender → floor lepas → tampil Contender (demosi akhirnya terlihat).
        (last, floor, left) = Ranks.StepTierFloor(last, 1350, floor, left, grace);
        Assert.Equal(1, Ranks.ApplyFloor(Ranks.TierOrder(Ranks.TierFor(1350)), floor, left));
    }

    [Fact]
    public void StepTierFloor_RepromotionResetsGrace()
    {
        const int grace = 3;
        int last = 1, floor = 0, left = 0;
        (last, floor, left) = Ranks.StepTierFloor(last, 1450, floor, left, grace); // → Rival, grace 3
        (last, floor, left) = Ranks.StepTierFloor(last, 1350, floor, left, grace); // turun, grace 2
        Assert.Equal(2, left);
        (last, floor, left) = Ranks.StepTierFloor(last, 1450, floor, left, grace); // naik Rival lagi → reset
        Assert.Equal((2, 2, 3), (last, floor, left));
    }
}
