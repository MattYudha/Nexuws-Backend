using Photon.Deterministic;

namespace Quantum
{
    /// <summary>
    /// Akhir match (T2 / NX-11). SATU tempat yang mengubah State 1 → 2, selalu bersama sebab & pemenang:
    /// KO target, waktu habis (skor → gear rontok → darah → seri), atau forfeit (AFK 20 dtk).
    /// Skor tidak pernah dipalsukan — backend memvalidasi kills = skor (docs/32).
    /// Dipanggil dari sistem yang sudah terdaftar (tanpa sistem baru → tak perlu buat ulang scene).
    /// </summary>
    public static unsafe class NxMatch
    {
        public const byte ReasonKo = 1, ReasonTime = 2, ReasonForfeit = 3;
        public const byte Draw = 255;

        /// <summary>Dipanggil sekali saat State 0 → 1.</summary>
        public static void Begin(Frame f)
        {
            // Semua ambang NxTuning dalam tick @TickRate. Sesi dengan UpdateFPS lain → durasi bergeser: peringatkan.
            if (f.UpdateRate != NxTuning.TickRate)
                Log.Warn($"NxTuning.TickRate={NxTuning.TickRate} tapi UpdateFPS sesi={f.UpdateRate}: timer/AFK/Overload tidak sesuai detik.");
            f.Global->StartTick = f.Number;
            f.Global->TimeLeftTicks = NxTuning.MatchTicks;
            f.Global->EndReason = 0;
            f.Global->WinnerTeam = Draw;
            f.Global->KoCount = 0;
        }

        /// <summary>Akhiri match sekali saja (KO, waktu habis, atau forfeit di tick yang sama → yang pertama menang).</summary>
        public static void End(Frame f, byte reason, byte winnerTeam)
        {
            if (f.Global->State != 1) return;
            f.Global->State = 2;
            f.Global->EndReason = reason;
            f.Global->WinnerTeam = winnerTeam;
        }

        /// <summary>Catat KO ke koTimeline + cek KO target. Dipanggil NxDamage.Ko SETELAH skor naik.</summary>
        public static void OnKo(Frame f, byte killerTeam)
        {
            if (f.Global->KoCount < NxTuning.KoLogCapacity)
            {
                var ev = f.Global->KoLog.GetPointer(f.Global->KoCount);
                ev->Tick = f.Number - f.Global->StartTick;
                ev->KillerSlot = killerTeam;
                f.Global->KoCount++;
            }
            if (f.Global->ScoreA >= NxCombatSystem.KoTarget) End(f, ReasonKo, 0);
            else if (f.Global->ScoreB >= NxCombatSystem.KoTarget) End(f, ReasonKo, 1);
        }

        /// <summary>Hitung mundur waktu match; dipanggil sekali per tick (NxHitSystem, sebelum hit).</summary>
        public static void TickTimer(Frame f)
        {
            if (f.Global->State != 1) return;
            if (f.Global->TimeLeftTicks > 0) f.Global->TimeLeftTicks--;
            if (f.Global->TimeLeftTicks == 0) End(f, ReasonTime, TimeUpWinner(f));
        }

        /// <summary>
        /// Waktu habis (keputusan user 2026-11-13): skor KO lebih tinggi menang; seri → tim dengan gear rontok
        /// TERBANYAK kalah; masih seri → tim dengan total darah PALING SEDIKIT kalah; masih seri → seri.
        /// </summary>
        public static byte TimeUpWinner(Frame f)
        {
            if (f.Global->ScoreA != f.Global->ScoreB) return f.Global->ScoreA > f.Global->ScoreB ? (byte)0 : (byte)1;

            int gear0 = 0, gear1 = 0;
            FP hp0 = 0, hp1 = 0;
            foreach (var (e, c) in f.Unsafe.GetComponentBlockIterator<NxCombat>())
            {
                if (c->Team == 0) { gear0 += c->GearsLost; hp0 += c->Hp; }
                else { gear1 += c->GearsLost; hp1 += c->Hp; }
            }
            if (gear0 != gear1) return gear0 > gear1 ? (byte)1 : (byte)0;
            if (hp0 != hp1) return hp0 < hp1 ? (byte)1 : (byte)0;
            return Draw;
        }
    }
}
