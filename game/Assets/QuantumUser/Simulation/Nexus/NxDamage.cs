using Photon.Deterministic;

namespace Quantum
{
    /// <summary>
    /// Resolusi damage BERSAMA untuk melee (NxHitSystem) dan projectile (NxProjectileSystem).
    /// Satu sumber kebenaran: Guard 180 (busur depan) → crush/faint, else
    /// Damage Gate (HP clamp 0) → Critical 3 Break Stock → Gear Drop → KO/respawn + knockback.
    /// Deterministik — tanpa float, tanpa RNG non-seeded.
    /// </summary>
    public static unsafe class NxDamage
    {
        public const int HitStunTicks = 14;        // >=10 (doc 08 §5.1)
        public const int HitPauseTicks = 8;
        public const int GuardStunTicks = 8;
        public const int FaintTicks = 90;
        public const byte GuardCostPerHit = 34;    // ~3 block → crush
        public const int CritKnockdownStun = 30;

        /// <summary>
        /// Terapkan satu hit ke korban dari posisi penyerang <paramref name="fromPos"/>.
        /// Return true bila KONTAK (kena ATAU tertangkis guard) — penyerang harus hitpause.
        /// Return false bila meleset (korban i-frame/faint) — proyektil tetap lanjut.
        /// </summary>
        public static bool ApplyHit(Frame f, byte attackerTeam, PlayerRef attackerPlayer, FPVector3 fromPos,
                                    EntityRef vicE, NxCombat* vic, FP damage, FP knockback, FP launchUp, int hitStun)
        {
            if (vic->Invuln > 0 || vic->Faint > 0) return false;

            // ---- Guard 180: dari busur depan korban → tertangkis ----
            if (vic->Guarding == 1 && InFrontArc(vic, fromPos))
            {
                if (vic->GuardGauge <= GuardCostPerHit)
                {
                    // Guard Crush → Faint (terbuka).
                    vic->GuardGauge = 0; vic->Guarding = 0; vic->Faint = FaintTicks; vic->HitPause = HitPauseTicks;
                }
                else
                {
                    vic->GuardGauge = (byte)(vic->GuardGauge - GuardCostPerHit);
                    vic->HitPause = GuardStunTicks;
                }
                return true;
            }

            // ---- Kena ----
            vic->HitPause = HitPauseTicks;
            FPVector3 push = vic->Pos - fromPos; push.Y = 0;
            push = push.SqrMagnitude > FP._0_01 ? push.Normalized : new FPVector3(0, 0, 1);

            if (vic->Critical == 0)
            {
                // Damage Gate: HP berhenti di 0 → masuk Critical.
                vic->Hp -= damage;
                if (vic->Hp <= 0)
                {
                    vic->Hp = 0; vic->Critical = 1; vic->BreakStock = 3; vic->CritTimer = 0;
                    vic->HitStun = (ushort)CritKnockdownStun; vic->Invuln = (byte)CritKnockdownStun;
                    vic->Vel = push * knockback + new FPVector3(0, launchUp, 0); vic->Posture = 1;
                }
                else
                {
                    vic->HitStun = (ushort)hitStun;
                    vic->Vel = push * knockback;
                }
            }
            else
            {
                // Critical: tiap pukul berat pecahkan 1 Break Stock + rontokkan 1 gear.
                if (vic->BreakStock > 0) vic->BreakStock--;
                DropOneGear(vic);
                vic->HitStun = (ushort)CritKnockdownStun; vic->Invuln = (byte)CritKnockdownStun;
                vic->Vel = push * knockback + new FPVector3(0, launchUp, 0); vic->Posture = 1;

                if (vic->BreakStock == 0) Ko(f, attackerTeam, attackerPlayer, vicE, vic);
            }
            return true;
        }

        public static bool InFrontArc(NxCombat* vic, FPVector3 attackerPos)
        {
            FPVector3 facing = new FPVector3(FPMath.Sin(vic->GuardYaw), 0, FPMath.Cos(vic->GuardYaw));
            FPVector3 to = attackerPos - vic->Pos; to.Y = 0;
            if (to.SqrMagnitude <= FP._0_01) return true;
            return FPVector3.Dot(facing, to.Normalized) > 0; // busur depan 180°
        }

        private static void DropOneGear(NxCombat* vic)
        {
            // Urutan: Helm(2) → Trinket(8) → Armor(4). Weapon(1) tidak rontok sebelum KO.
            if ((vic->GearMask & 2) != 0) vic->GearMask &= unchecked((byte)~2);
            else if ((vic->GearMask & 8) != 0) vic->GearMask &= unchecked((byte)~8);
            else if ((vic->GearMask & 4) != 0) vic->GearMask &= unchecked((byte)~4);
            // (Spike: dropped-gear entity + pickup menyusul.)
        }

        private static void Ko(Frame f, byte killerTeam, PlayerRef killerPlayer, EntityRef vicE, NxCombat* vic)
        {
            vic->Kos++; // death korban
            if (killerTeam == 0) f.Global->ScoreA++; else f.Global->ScoreB++; // skor per-tim
            // Kredit kill ke penyerang (kalau pemain manusia) untuk statistik profil.
            if (killerPlayer != PlayerRef.None)
                foreach (var (e, o) in f.Unsafe.GetComponentBlockIterator<NxCombat>())
                    if (o->Player == killerPlayer) { o->Kills++; break; }
            Respawn(f, vic);
        }

        /// <summary>Respawn penuh di sisi tim sendiri, posisi disebar (anti-numpuk) + i-frame sebentar.</summary>
        public static void Respawn(Frame f, NxCombat* vic)
        {
            vic->Hp = NxCombatSystem.MaxHp; vic->Critical = 0; vic->BreakStock = 0; vic->GearMask = 0xF;
            vic->GuardGauge = 100; vic->HitStun = 40; vic->Invuln = 40; vic->Faint = 0; vic->CritTimer = 0;
            FP x = vic->Team == 0 ? -6 : 6;
            FP z = ((vic->Kos % 5) - 2) * 2; // sebar di sumbu Z berdasar jumlah KO (deterministik)
            vic->Pos = new FPVector3(x, 0, z); vic->Vel = default; vic->Posture = 0;
        }
    }
}
