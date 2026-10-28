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
                    if (vic->HitStun > 0 || vic->Invuln > 0 || vic->Faint > 0) continue;
                    if ((vic->Pos - center).SqrMagnitude > HitRange * HitRange) continue;

                    bool contact = NxDamage.ApplyHit(f, att->Team, att->Player, att->Pos, vicE, vic,
                                                     HitDamage, Knockback, LaunchUp, NxDamage.HitStunTicks);
                    if (contact)
                    {
                        att->AttackTime = 0;
                        att->HitPause = (ushort)NxDamage.HitPauseTicks;
                        break; // satu ayunan = satu kontak
                    }
                }
            }

            // Win-condition RANKED: tim pertama capai KoTarget KO → match selesai.
            // Hanya aktif bila ada >=2 pemain manusia (practice 1 pemain = tak berujung).
            // Ditaruh di sini (sistem yang SELALU terdaftar) agar tidak bergantung pada NxProjectileSystem.
            int humans = 0;
            foreach (var (_, c) in f.Unsafe.GetComponentBlockIterator<NxCombat>())
                if (c->Player != PlayerRef.None) humans++;
            if (humans >= 2 &&
                (f.Global->ScoreA >= NxCombatSystem.KoTarget || f.Global->ScoreB >= NxCombatSystem.KoTarget))
                f.Global->State = 2; // Over
        }
    }
}
