namespace PhotonBackend.Api;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "photon-backend";
    public string Secret { get; set; } = "";
    public int LifetimeHours { get; set; } = 24;
}

public sealed class PhotonOptions
{
    public const string Section = "Photon";

    /// <summary>Shared secret Photon sends in the X-SecretKey header (dashboard key "WebHookSecret").</summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>Shared secret appended as ?secret= to the custom authentication URL in the dashboard.</summary>
    public string AuthSecret { get; set; } = "";
}

/// <summary>Akses admin/dev (mis. reset faksi). Kosong = semua endpoint admin nonaktif.</summary>
public sealed class AdminOptions
{
    public const string Section = "Admin";
    public string Secret { get; set; } = ""; // dikirim di header X-Admin-Secret
}

public sealed class GameOptions
{
    public const string Section = "Game";

    public int WinScore { get; set; } = 5;          // target KO (Nexus 1v1) / target poin (Pong)
    public int EloK { get; set; } = 32;             // lama (Elo) — tak dipakai sejak Glicko-2
    public int StartRating { get; set; } = 1500;    // konvensi Glicko-2
    public double StartRd { get; set; } = 350.0;    // RD awal (pemain baru = tak pasti)
    public double StartVolatility { get; set; } = 0.06;
    public double RdMin { get; set; } = 30.0;       // D12: lantai RD → rating−2·RD tetap bermakna
    public double RdMax { get; set; } = 350.0;      // D12: batas atas inflasi RD (= RD pemain baru)
    public int RdInactivityPeriodDays { get; set; } = 7; // D12: tiap periode tanpa match ranked → RD naik (Glickman langkah 6)
    public double Tau { get; set; } = 0.5;          // konstanta sistem Glicko-2 (0.3–1.2)
    public int MatchmakingWindow { get; set; } = 400; // (lama; dipakai sbg Start default)

    // --- Matchmaking: rentang rating melebar bertahap + batas tunggu + versi + debug ---
    public int MatchmakingWindowStart { get; set; } = 400;        // rentang rating awal
    public int MatchmakingWindowGrowthPerSec { get; set; } = 40;  // pelebaran per detik menunggu
    public int MatchmakingMaxWaitSeconds { get; set; } = 75;      // setelah ini: terima lawan mana pun (region+versi sama)
    public bool MatchmakingIgnoreRating { get; set; } = false;    // DEBUG dev: abaikan rentang rating (dua akun uji selalu bertemu)
    public int MatchmakingWaiterStaleSeconds { get; set; } = 10;  // penunggu tanpa poll (klien ~1 dtk) selama ini → dibuang (anti pemain hantu)
    public int MatchmakingAssignmentTtlSeconds { get; set; } = 180; // assignment basi (klien crash sebelum /leave) → dilepas. > pilih hero + match 100 dtk + lapor (Q42)
    public string RequiredSimVersion { get; set; } = "";         // kalau diisi: join wajib simVersion sama (tolak build lama)
    public string RequiredBuildVersion { get; set; } = "";       // opsional: samakan versi build juga

    // --- Sanity-check & anti-kolusi hasil ranked (docs/32 KEAMANAN MINIMUM) ---
    public bool AllowTestAccounts { get; set; } = false; // true HANYA di dev/test → izinkan buat akun dgn flag IsTest
    public int MinMatchSeconds { get; set; } = 8;        // durasi di bawah ini → hasil mencurigakan (tolak)
    public double MaxKoPerSecond { get; set; } = 1.0;    // laju KO di atas ini → mencurigakan (tolak)
    public int CollusionWindowMinutes { get; set; } = 30; // jendela deteksi pasangan berulang
    public int CollusionThreshold { get; set; } = 3;     // >= sekian pertemuan dalam jendela → redam rating
    public double CollusionRatingFactor { get; set; } = 0.25; // faktor pengali perolehan rating saat diredam

    // --- Proteksi demosi (docs/33 opsi 1) ---
    // NONAKTIF selama development: 0 = floor tak pernah aktif → tier tampil = tier rating asli.
    // Kode/kolom DB/tes dipertahankan. AKTIFKAN KEMBALI (3) SEBELUM RILIS PUBLIK.
    public int DemotionGraceMatches { get; set; } = 0;   // setelah naik tier: tier TAMPIL tak turun sekian match ranked

    // --- Faksi ORDE/LEGION (docs/35) ---
    // Kunci: pilihan pertama bebas; PINDAH gratis 1× selama placement <5 ranked (LeaderboardService.PlacementMatches), lalu terkunci.
    public int FactionMinPopulation { get; set; } = 2;       // di bawah ini: tak ada "Butuh Pejuang" (hindari noise awal)
    public double FactionImbalanceThreshold { get; set; } = 0.45; // share minoritas < ini → "Butuh Pejuang"
    public int FactionJoinBonusPercent { get; set; } = 15;   // bonus XP utk pemain faksi minoritas (insentif seimbang)
}
