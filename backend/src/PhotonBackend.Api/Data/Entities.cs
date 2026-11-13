namespace PhotonBackend.Api.Data;

public sealed class Player
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? DeviceId { get; set; }          // guest (anon device); null untuk akun username
    public string? Username { get; set; }           // akun terdaftar; null untuk guest
    public string? PasswordHash { get; set; }       // hash (ASP.NET PasswordHasher)
    public string DisplayName { get; set; } = "";
    public int Faction { get; set; }                // 0 none, 1 ORDE, 2 LEGION
    public bool FactionSwitchUsed { get; set; }     // jatah pindah faksi GRATIS (1×, selama placement <5 ranked) sudah dipakai (docs/35)
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

/// <summary>
/// Jejak audit setiap perubahan faksi lewat endpoint admin (docs/35). Siapa (sumber), pemain, lama→baru, kapan.
/// UTANG RILIS: ganti X-Admin-Secret dengan admin BERBASIS ROLE + audit log resmi (docs/90, docs/91) sebelum publik.
/// </summary>
public sealed class FactionAdminAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlayerId { get; set; }
    public int OldFaction { get; set; }              // 0 none, 1 ORDE, 2 LEGION
    public int NewFaction { get; set; }
    public string AdminSource { get; set; } = "";    // sumber dev (label opsional + IP); peran/identitas resmi menyusul
    public DateTime At { get; set; } = DateTime.UtcNow;
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
    public string EndReason { get; set; } = "";    // "ko" | "time" | "forfeit" ("" = match lama)
    public bool IsDraw { get; set; }               // seri (waktu habis/forfeit tanpa pemenang) → rating tak berubah
    public string? KoTimeline { get; set; }        // JSON [{Tick,KillerSlot}] ≤9, disepakati kedua klien (tab "Jalannya Match")
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
    public string? ReportedEndReason { get; set; }   // laporan klien ini; harus sama antar-klien
    public int? ReportedWinnerSlot { get; set; }     // 0/1, -1 = seri
    public string? ReportedKoTimeline { get; set; }  // JSON kanonik; harus sama antar-klien
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

/// <summary>
/// Pengumuman/Event lobby (docs/31 §1.7). Dibuat admin (X-Admin-Secret), sumber tercatat. Dihapus = soft delete
/// (Removed) agar jejak tetap ada. Tanpa baris aktif → lobby menampilkan "Belum ada pengumuman" (tanpa data dummy).
/// </summary>
public sealed class Announcement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "info";      // "info" | "event"
    public string Title { get; set; } = "";         // 1..80
    public string Body { get; set; } = "";          // 0..400
    public DateTime StartsAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndsAt { get; set; }           // null = tanpa batas
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "";     // sumber admin@ip (maks 160)
    public bool Removed { get; set; }
}
