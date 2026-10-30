using System;

namespace PhotonBackend.Api.Services;

/// <summary>
/// Pemetaan progres pemain (doc 24 §2.3 + doc 15 §3):
/// - Solo 1v1 ladder tier (dari Glicko-2 rating): Rookie→Nexus Legend (tema).
/// - Account Rank (dari accountXp/level): Trainee→Colonel (pangkat, nol efek kompetitif).
/// </summary>
public static class Ranks
{
    // Tier ladder Solo 1v1 (rating Glicko-2, center 1500).
    public static string TierFor(int rating) => rating switch
    {
        < 1200 => "Rookie",
        < 1400 => "Contender",
        < 1600 => "Rival",
        < 1800 => "Gladiator",
        < 2000 => "Conqueror",
        _ => "Nexus Legend",
    };

    // Urutan tier untuk sorting (0 Rookie … 5 Nexus Legend). Makin tinggi makin atas.
    public static int TierOrder(string tier) => tier switch
    {
        "Contender" => 1, "Rival" => 2, "Gladiator" => 3, "Conqueror" => 4, "Nexus Legend" => 5, _ => 0,
    };

    // Kebalikan TierOrder: urutan → nama tier.
    public static string TierName(int order) => order switch
    {
        1 => "Contender", 2 => "Rival", 3 => "Gladiator", 4 => "Conqueror", 5 => "Nexus Legend", _ => "Rookie",
    };

    // --- PROTEKSI DEMOSI (docs/33 opsi 1) ---
    // Floor di LAYER TAMPILAN saja; Glicko (rating/RD) tetap murni. Setelah naik tier, tier yang TAMPIL
    // tak boleh turun selama `DemotionGraceMatches` match ranked berikutnya. Dipakai di profil & leaderboard.

    /// <summary>Tier yang DITAMPILKAN: naikkan ke floor bila proteksi masih aktif, jangan pernah turunkan.</summary>
    public static int ApplyFloor(int baseOrder, int floorOrder, int floorLeft)
        => floorLeft > 0 ? System.Math.Max(baseOrder, floorOrder) : baseOrder;

    /// <summary>
    /// Perbarui state floor setelah satu match ranked (fungsi murni → mudah diuji).
    /// Naik tier → pasang floor di tier baru + reset grace. Selain itu → kurangi grace; habis → floor lepas.
    /// `lastTier` selalu = tier mentah terkini (untuk deteksi promosi match berikutnya).
    /// </summary>
    public static (int lastTier, int floorOrder, int floorLeft) StepTierFloor(
        int prevLastTier, int newRating, int floorOrder, int floorLeft, int grace)
    {
        int raw = TierOrder(TierFor(newRating));
        if (raw > prevLastTier)                        // promosi (termasuk promosi ulang saat proteksi aktif)
        {
            int g = System.Math.Max(0, grace);
            return (raw, g > 0 ? raw : 0, g);          // grace 0 (NONAKTIF) → tak ada floor
        }
        int left = floorLeft > 0 ? floorLeft - 1 : 0;  // stagnan/turun → grace berkurang
        return (raw, left > 0 ? floorOrder : 0, left);
    }

    // "Poin Ladder" 0–100: progres di DALAM tier (bukan MMR mentah). Band 200 lebar; Rookie/Legend dibatasi.
    public static int LadderPoints(int rating)
    {
        int lo = rating switch
        {
            < 1200 => 1000, < 1400 => 1200, < 1600 => 1400, < 1800 => 1600, < 2000 => 1800, _ => 2000,
        };
        int hi = lo + (lo >= 2000 ? 400 : 200); // Legend: 2000–2400
        int pts = (int)System.Math.Round(100.0 * (rating - lo) / (hi - lo));
        return System.Math.Clamp(pts, 0, 100);
    }

    // Pencocokan NAMA untuk backfill SATU KALI akun uji LAMA (E2E_*, FB_*, GATE, PersistTest, logintest_*).
    // BUKAN aturan pembuatan akun: akun baru default IsTestAccount=false; flag di-set EKSPLISIT via jalur tes
    // (Game:AllowTestAccounts, hanya dev/test). Jadi login asli masa depan (mis. Facebook) TIDAK ikut tertandai.
    public static bool IsLegacyTestName(string? username, string? displayName)
    {
        string u = ((username ?? "") + "|" + (displayName ?? "")).ToLowerInvariant();
        string[] marks = { "e2e_", "e2e-", "fb_", "fb-", "gate", "persisttest", "logintest" };
        foreach (var m in marks) if (u.Contains(m)) return true;
        return false;
    }

    // Account Rank (pangkat militer dari level akun).
    public static string AccountRankFor(int level) => level switch
    {
        < 5 => "Trainee",
        < 10 => "Private",
        < 20 => "Corporal",
        < 35 => "Sergeant",
        < 55 => "Lieutenant",
        < 80 => "Captain",
        < 110 => "Major",
        _ => "Colonel",
    };

    public const int XpPerLevel = 100;
    public static int LevelFor(int xp) => Math.Max(1, xp / XpPerLevel + 1);
}
