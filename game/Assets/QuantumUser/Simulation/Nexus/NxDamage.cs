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

        public enum HitKind : byte { Miss, Dodged, Blocked, Hit, Ko }

        /// <summary>Cari NxCombat* milik satu player (untuk atribusi statistik proyektil). Null bila bot/None.</summary>
        public static NxCombat* FindByPlayer(Frame f, PlayerRef player)
        {
            if (player == PlayerRef.None) return null;
            foreach (var (e, o) in f.Unsafe.GetComponentBlockIterator<NxCombat>())
                if (o->Player == player) return o;
            return null;
        }

        /// <summary>
        /// Terapkan satu hit ke korban dari posisi penyerang <paramref name="fromPos"/>.
        /// <paramref name="atk"/> = komponen penyerang (boleh null untuk bot) → untuk atribusi statistik.
        /// Return <see cref="HitKind"/>: Miss/Dodged = tak kontak; Blocked/Hit/Ko = kontak (penyerang hitpause).
        /// Semua akumulator statistik (docs/32) ditulis di sini sebagai SATU sumber kebenaran.
        /// </summary>
        public static HitKind ApplyHit(Frame f, NxCombat* atk, byte attackerTeam, PlayerRef attackerPlayer, FPVector3 fromPos,
                                       EntityRef vicE, NxCombat* vic, FP damage, FP knockback, FP launchUp, int hitStun, bool isSkill)
        {
            if (vic->Invuln > 0 || vic->Faint > 0)
            {
                if (vic->DashTime > 0) { vic->Dodges++; return HitKind.Dodged; } // lolos karena i-frame dash
                return HitKind.Miss;
            }

            // ---- Guard 180: dari busur depan korban → tertangkis ----
            if (vic->Guarding == 1 && InFrontArc(vic, fromPos))
            {
                vic->HitsBlocked++;
                // T2: guard = keterlibatan aktif → Overload tidak boleh terpicu karena lawan menahan guard.
                vic->OverloadCharge = 0; vic->OverloadLock = 0;
                if (vic->GuardGauge <= GuardCostPerHit)
                {
                    // Guard Crush → Faint (terbuka).
                    vic->GuardGauge = 0; vic->Guarding = 0; vic->Faint = FaintTicks; vic->HitPause = HitPauseTicks;
                    if (atk != null) atk->GuardCrushes++;
                }
                else
                {
                    vic->GuardGauge = (byte)(vic->GuardGauge - GuardCostPerHit);
                    vic->HitPause = GuardStunTicks;
                }
                return HitKind.Blocked;
            }

            // ---- Kena ----
            vic->HitPause = HitPauseTicks;
            // T2: kena pukul mereset Overload (timer & lock) — Overload hanya untuk lawan yang mendiamkan.
            vic->OverloadCharge = 0; vic->OverloadLock = 0;
            FPVector3 push = vic->Pos - fromPos; push.Y = 0;
            push = push.SqrMagnitude > FP._0_01 ? push.Normalized : new FPVector3(0, 0, 1);

            HitKind kind = HitKind.Hit;
            if (vic->Critical == 0)
            {
                // Damage Gate: HP berhenti di 0 → masuk Critical.
                vic->Hp -= damage;
                vic->DamageTaken += damage;
                if (atk != null) { atk->DamageDealt += damage; if (atk->HeroId == 0) atk->H0Dmg += damage; else atk->H1Dmg += damage; if (isSkill) atk->SkillsHit++; }
                if (vic->Hp <= 0)
                {
                    vic->Hp = 0; vic->Critical = 1; vic->BreakStock = 3;
                    vic->OverloadCharge = 0; vic->OverloadLock = 0;   // T2: masuk Critical = mulai bersih
                    vic->HitStun = (ushort)CritKnockdownStun; vic->Invuln = (byte)CritKnockdownStun;
                    vic->Vel = push * knockback + new FPVector3(0, launchUp, 0); vic->Posture = 1;
                    if (atk != null) atk->Criticals++;
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
                if (atk != null) { atk->GearDrops++; if (isSkill) atk->SkillsHit++; }
                vic->HitStun = (ushort)CritKnockdownStun; vic->Invuln = (byte)CritKnockdownStun;
                vic->Vel = push * knockback + new FPVector3(0, launchUp, 0); vic->Posture = 1;

                if (vic->BreakStock == 0) { Ko(f, attackerTeam, attackerPlayer, vicE, vic); kind = HitKind.Ko; }
            }
            return kind;
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
            byte before = vic->GearMask;
            if ((vic->GearMask & 2) != 0) vic->GearMask &= unchecked((byte)~2);
            else if ((vic->GearMask & 8) != 0) vic->GearMask &= unchecked((byte)~8);
            else if ((vic->GearMask & 4) != 0) vic->GearMask &= unchecked((byte)~4);
            if (vic->GearMask != before && vic->GearsLost < ushort.MaxValue) vic->GearsLost++; // tie-break waktu habis
            // (Spike: dropped-gear entity + pickup menyusul.)
        }

        public static void Ko(Frame f, byte killerTeam, PlayerRef killerPlayer, EntityRef vicE, NxCombat* vic)
        {
            bool vicHuman = vic->Player != PlayerRef.None;
            vic->Kos++;            // death korban
            vic->CurStreak = 0;    // streak korban putus
            if (killerTeam == 0) f.Global->ScoreA++; else f.Global->ScoreB++; // skor per-tim (first-to-5)
            // Kredit kill ke penyerang (kalau pemain manusia) + statistik.
            if (killerPlayer != PlayerRef.None)
                foreach (var (e, o) in f.Unsafe.GetComponentBlockIterator<NxCombat>())
                    if (o->Player == killerPlayer)
                    {
                        if (vicHuman) { o->Kills++; if (o->HeroId == 0) { if (o->H0Kills < 255) o->H0Kills++; } else { if (o->H1Kills < 255) o->H1Kills++; } }
                        else { if (o->BotKills < ushort.MaxValue) o->BotKills++; }
                        o->CurStreak++; if (o->CurStreak > o->MaxStreak) o->MaxStreak = o->CurStreak;
                        if (vicHuman && f.Global->FirstBloodTaken == 0) { o->FirstBlood = 1; f.Global->FirstBloodTaken = 1; }
                        break;
                    }
            Respawn(f, vic);
            NxMatch.OnKo(f, killerTeam); // koTimeline + KO target → State 2 (T2)
        }

        /// <summary>Respawn penuh di sisi tim sendiri, posisi disebar (anti-numpuk) + i-frame sebentar.</summary>
        public static void Respawn(Frame f, NxCombat* vic)
        {
            vic->Hp = NxCombatSystem.MaxHp; vic->Critical = 0; vic->BreakStock = 0; vic->GearMask = 0xF;
            vic->GuardGauge = 100; vic->HitStun = 40; vic->Invuln = 40; vic->Faint = 0;
            // T2: respawn membersihkan status tekanan. IdleTicks SENGAJA tidak direset:
            // pemain AFK yang terus di-KO tetap harus kena forfeit 20 dtk.
            vic->OverloadCharge = 0; vic->OverloadLock = 0;
            FP x = vic->Team == 0 ? -6 : 6;
            FP z = ((vic->Kos % 5) - 2) * 2; // sebar di sumbu Z berdasar jumlah KO (deterministik)
            vic->Pos = new FPVector3(x, 0, z); vic->Vel = default; vic->Posture = 0;
        }
    }
}
