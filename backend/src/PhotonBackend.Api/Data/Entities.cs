namespace PhotonBackend.Api.Data;

public sealed class Player
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? DeviceId { get; set; }          // guest (anon device); null untuk akun username
    public string? Username { get; set; }           // akun terdaftar; null untuk guest
    public string? PasswordHash { get; set; }       // hash (ASP.NET PasswordHasher)
    public string DisplayName { get; set; } = "";
    public int Faction { get; set; }                // 0 none, 1 ORDE, 2 LEGION
    public int Rating { get; set; }
    public double Rd { get; set; } = 350.0;        // Glicko-2 rating deviation (keyakinan)
    public double Volatility { get; set; } = 0.06; // Glicko-2 volatility
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Kills { get; set; }                  // total KO yang dilakukan
    public int Deaths { get; set; }                 // total ter-KO
    public int Assists { get; set; }
    public int AccountXp { get; set; }              // dari semua main → Account Rank (nol efek kompetitif)
    public bool IsTestAccount { get; set; }         // akun uji (E2E/FB/curl/dll) → TIDAK tampil di leaderboard

    // --- Proteksi demosi (docs/33 opsi 1): floor tier LAYER TAMPILAN; Glicko tetap murni. ---
    public int LastRankedTierOrder { get; set; }     // tier mentah (urutan) setelah match ranked terakhir → deteksi promosi
    public int TierFloorOrder { get; set; }          // floor tier (urutan) yang sedang dilindungi (0 = tak ada)
    public int TierFloorLeft { get; set; }           // sisa match ranked floor masih berlaku (0 = lepas)

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class MatchStatus
{
    public const string Created = "Created";
    public const string Finished = "Finished";
    public const string Disputed = "Disputed";
    public const string Closed = "Closed";
}

public sealed class Match
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Photon room name / game id. Unique per match.</summary>
    public string GameId { get; set; } = "";
    public string Region { get; set; } = "";
    public string Status { get; set; } = MatchStatus.Created;
    public int? ScoreLeft { get; set; }
    public int? ScoreRight { get; set; }
    public string Mode { get; set; } = "";       // "ranked" | "battle" | ...
    public string Format { get; set; } = "";      // "ranked1v1" | "battle" | "legacy5v5" (lama, K/D tak valid)
    public int DurationSec { get; set; }           // durasi match (detik)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }

    /// <summary>Raw body of the last Photon GameResult webhook, kept for auditing only.</summary>
    public string? PhotonResultRaw { get; set; }

    public List<MatchPlayer> Players { get; set; } = new();
}

public sealed class MatchPlayer
{
    public Guid MatchId { get; set; }
    public Match Match { get; set; } = null!;
    public Guid PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    /// <summary>0 = left paddle, 1 = right paddle.</summary>
    public int Slot { get; set; }
    public int? ReportedScoreLeft { get; set; }
    public int? ReportedScoreRight { get; set; }
    public int ReportedKills { get; set; }
    public int ReportedDeaths { get; set; }
    public int ReportedAssists { get; set; }
    public bool IsWinner { get; set; }
    public int RatingDelta { get; set; }
    public int RatingBefore { get; set; }          // rating sebelum match (ranked) → After = Before + Delta
    public int HeroId { get; set; }                // hero DOMINAN pemain ini (playTicks terbanyak)
    public int PartySize { get; set; } = 1;        // ukuran party pemain ini (1 = solo)
    public Guid? PartyId { get; set; }             // id party (null sekarang; untuk badge PARTY nanti)

    // --- Statistik akumulator per match (docs/32 Lapisan 1). Null/0 untuk match lama → UI "–". ---
    public int BotKills { get; set; }              // KO terhadap bot (dipisah; tak dihitung ranked/leaderboard)
    public int DamageDealt { get; set; }
    public int DamageTaken { get; set; }
    public int SkillsUsed { get; set; }
    public int SkillsHit { get; set; }
    public int HitsBlocked { get; set; }
    public int Dodges { get; set; }
    public int GuardCrushes { get; set; }
    public int Criticals { get; set; }
    public int GearDrops { get; set; }
    public bool FirstBlood { get; set; }
    public int MaxKillStreak { get; set; }
    public int MaxDeficit { get; set; }
    public int HeroSwitches { get; set; }
    public bool HasDetailedStats { get; set; }     // true bila payload akumulator diterima (bedakan match lama → "–")

    /// <summary>Payload hasil LENGKAP (JSON kanonik) yang DILAPORKAN pemain ini → dibandingkan antar-klien (docs/32 KEAMANAN).</summary>
    public string? ResultPayload { get; set; }

    public List<MatchPlayerHero> Heroes { get; set; } = new();
}

/// <summary>Statistik per-hero dalam SATU match (karena bisa ganti hero). Sumber Hero Favorit (docs/32).</summary>
public sealed class MatchPlayerHero
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MatchId { get; set; }
    public Guid PlayerId { get; set; }
    public MatchPlayer MatchPlayer { get; set; } = null!;
    public int HeroId { get; set; }
    public int PlayTicks { get; set; }
    public int Kills { get; set; }
    public int Damage { get; set; }
}
