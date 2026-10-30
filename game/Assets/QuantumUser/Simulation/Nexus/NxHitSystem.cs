using Photon.Deterministic;

namespace Quantum
{
    /// <summary>
    /// Hit MELEE (SPIKE v2): penyerang aktif (AttackTime&gt;0) → cek hurtbox lawan (sphere depan),
    /// lalu resolusi via <see cref="NxDamage.ApplyHit"/> (Guard/Critical/Gear/KO bersama projectile).
    /// Dipakai TITAN-01 (grappler) & lunge-skill-nya. PULSE-X pakai projectile (tak set AttackTime).
    /// </summary>
    public unsafe class NxHitSystem : SystemMainThread
    {
        private static readonly FP HitRange = 2;
        private static readonly FP HitDamage = 18;
        private static readonly FP Knockback = 6;
        private static readonly FP LaunchUp = 5;

        public override void Update(Frame f)
        {
            if (f.Global->State != 1) return;

            foreach (var (attE, att) in f.Unsafe.GetComponentBlockIterator<NxCombat>())
            {
                if (att->AttackTime <= 0) continue;
                FPVector3 fwd = new FPVector3(FPMath.Sin(att->Yaw), 0, FPMath.Cos(att->Yaw));
                FPVector3 center = att->Pos + fwd * HitRange;

                foreach (var (vicE, vic) in f.Unsafe.GetComponentBlockIterator<NxCombat>())
                {
                    if (vicE == attE || vic->Team == att->Team) continue;
                    if ((vic->Pos - center).SqrMagnitude > HitRange * HitRange) continue;
                    // Lolos lewat dash (i-frame) = dodge berhasil → ayunan whiff (dedup: 1 dodge per ayunan).
                    if (vic->DashTime > 0) { vic->Dodges++; att->AttackTime = 0; break; }
                    // i-frame lain (hitstun/getup/respawn/faint): tak bisa dipukul lagi (anti-juggle), bukan dodge.
                    if (vic->HitStun > 0 || vic->Invuln > 0 || vic->Faint > 0) continue;

                    var kind = NxDamage.ApplyHit(f, att, att->Team, att->Player, att->Pos, vicE, vic,
                                                 HitDamage, Knockback, LaunchUp, NxDamage.HitStunTicks, att->SkillSwing != 0);
                    if (kind == NxDamage.HitKind.Miss) continue;
                    // kontak (Blocked/Hit/Ko)
                    att->AttackTime = 0;
                    att->HitPause = (ushort)NxDamage.HitPauseTicks;
                    break; // satu ayunan = satu kontak
                }
            }

            // Win-condition: tim pertama capai KoTarget KO → match selesai. Selalu ada lawan
            // (ranked = 2 manusia; practice = 1 manusia + 1 bot; battle = tim isi bot), jadi tak berujung-mati.
            if (f.Global->ScoreA >= NxCombatSystem.KoTarget || f.Global->ScoreB >= NxCombatSystem.KoTarget)
                f.Global->State = 2; // Over
        }
    }
}
