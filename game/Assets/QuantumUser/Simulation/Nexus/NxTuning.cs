namespace Quantum
{
    /// <summary>
    /// Angka T2 (disetujui user 2026-11-13). SATU tempat untuk semua ambang —
    /// jangan hardcode di sistem. Semua dalam tick (60 Hz), tanpa float.
    /// Sumber: docs/90 2026-11-13 (NX-11) + docs/research/t2-spec/01-rules-and-numbers.md.
    /// </summary>
    public static class NxTuning
    {
        // --- Tempo simulasi ---
        public const int TickRate = 60;                // samakan dgn SessionConfig.UpdateFPS

        // --- Batas waktu match (NX-11, keputusan user 2026-11-13) ---
        public const int MatchSeconds = 100;
        public const int MatchTicks = MatchSeconds * TickRate;
        // Waktu habis: skor KO → gear rontok terbanyak KALAH → darah tersisa paling sedikit KALAH → seri.

        // --- AFK / forfeit (keputusan user 2026-11-16: baru kalah bila idle LEBIH dari 1 menit) ---
        public const int AfkWarnTicks = 60 * TickRate;      // 60 dtk: HUD memperingatkan
        public const int AfkForfeitTicks = 75 * TickRate;   // 75 dtk (>1 mnt): kalah WO (skor TIDAK dipalsukan). Harus < MatchTicks 100 dtk.
        // Input yang MERESET timer AFK: gerak, lompat, guard, dash, serang, skill.
        // Tukar hero (32) & pilih hero TIDAK dihitung. Mouse tidak dihitung.
        public const byte ActionButtons = 1 | 2 | 4 | 8 | 16;

        // --- koTimeline ("Jalannya Match") ---
        public const int KoLogCapacity = 9;            // first-to-5 → maks 5+4. Samakan dgn array di Nexus.qtn.

        // --- Overload (anti-camping saat Critical) ---
        public const int OverloadTriggerTicks = 480;  // 8 dtk tanpa kena damage
        public const int OverloadLockTicks = 90;      // 1,5 dtk terkunci (bukan KO)
        // TANPA pengali damage: sengaja (menunggu Overload tidak boleh lebih untung daripada menyerang).

        // --- Penalti gerak saat Critical (C4) ---
        // 70% sejak masuk Critical, 50% di stock 2, 35% di stock 1. Dash dikunci sejak Critical.
        // Diwakili per-mille agar integer (FP dibentuk di pemakaian: N/1000).
        public const int CritSpeedPermilleStock3 = 700;
        public const int CritSpeedPermilleStock2 = 500;
        public const int CritSpeedPermilleStock1 = 350;

        // --- Bot ---
        public const int BotCriticalBonus = 40;    // dikurangi dari skor jarak^2
        public const int BotOverloadBonus = 80;
        public const int BotCrowdPenalty = 30;     // per penyerang lain di sekitar target
        public const int BotCrowdRadius = 3;       // unit

        // --- Aim assist PULSE (user 2026-11-16): peluru dibantu ke musuh terdekat DI DEPAN (bukan homing) ---
        public static readonly Photon.Deterministic.FP AimAssistRange = 14;                 // unit jangkauan bantuan
        public static readonly Photon.Deterministic.FP AimAssistMinDot = Photon.Deterministic.FP._0_25; // ~75 derajat setengah-cone (hanya musuh di depan)
    }
}
