using System.Collections.Generic;
using Photon.Deterministic;

namespace Quantum
{
    /// <summary>
    /// Proyektil PULSE-X (SPIKE v2): gerak lurus tiap tick, cek sphere vs lawan (XZ),
    /// kena → <see cref="NxDamage.ApplyHit"/> lalu hancur; habis Life → hancur.
    /// Deterministik; destroy dikumpulkan dulu lalu dieksekusi setelah iterasi (aman).
    /// </summary>
    public unsafe class NxProjectileSystem : SystemMainThread
    {
        // Radius kontak ~1.35 (pakai konstanta FP yang sudah terbukti ada).
        private static readonly FP HitR = FP._0_50 + FP._0_50 + FP._0_25 + FP._0_10;
        private static readonly FP Knock = 2;   // dorongan kecil — tidak mendorong lawan jauh ke belakang
        private static readonly FP Launch = 0;
        private const int ProjStun = 5;         // hitstun RINGAN → melee tetap bisa maju (bukan stunlock)

        public override void Update(Frame f)
        {
            if (f.Global->State != 1) return;

            var dead = new List<EntityRef>();
            foreach (var (e, p) in f.Unsafe.GetComponentBlockIterator<NxProjectile>())
            {
                if (f.Global->State != 1) break;   // T2: match berakhir di tick ini → hentikan hit lain
                p->Pos += p->Vel * f.DeltaTime;

                bool hit = false;
                var atk = NxDamage.FindByPlayer(f, p->Owner); // null bila bot → statistik tak diatribusi
                foreach (var (vicE, vic) in f.Unsafe.GetComponentBlockIterator<NxCombat>())
                {
                    if (vic->Team == p->Team) continue;
                    FPVector3 dp = vic->Pos - p->Pos; dp.Y = 0;
                    if (dp.SqrMagnitude > HitR * HitR) continue;
                    var kind = NxDamage.ApplyHit(f, atk, p->Team, p->Owner, p->Pos, vicE, vic, p->Damage, Knock, Launch, ProjStun, p->IsSkill != 0);
                    if (kind == NxDamage.HitKind.Blocked || kind == NxDamage.HitKind.Hit || kind == NxDamage.HitKind.Ko)
                    {
                        hit = true; break;
                    }
                    // Miss (i-frame) / Dodged (dash): proyektil tembus — lanjut cek lawan lain.
                }

                if (p->Life > 0) p->Life--;
                if (hit || p->Life == 0) dead.Add(e);
            }

            foreach (var e in dead) f.Destroy(e);
        }
    }
}
