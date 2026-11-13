using Photon.Deterministic;

namespace Quantum
{
    /// <summary>
    /// Pushbox ringan antar-fighter (SPIKE v2): dorong halus badan yang tumpang-tindih agar tidak
    /// menumpuk jadi satu titik. Jalan SETELAH gerak/hit. Tiap pasangan hanya menggeser 'self'
    /// (saat pasangan dibalik, yang lain yang bergeser) → simetris, tanpa dobel, deterministik.
    /// Hanya XZ (tidak mengubah Y) → tidak menembus lantai.
    /// </summary>
    public unsafe class NxSeparationSystem : SystemMainThread
    {
        private static readonly FP MinDist = FP._0_50 + FP._0_50 + FP._0_50; // 1.5 (jarak min antar pusat)

        public override void Update(Frame f)
        {
            if (f.Global->State != 1) return;

            foreach (var (selfE, self) in f.Unsafe.GetComponentBlockIterator<NxCombat>())
            {
                if (self->HitPause > 0) continue;                       // hitstop: badan beku, jangan digeser keluar hitbox
                foreach (var (otherE, other) in f.Unsafe.GetComponentBlockIterator<NxCombat>())
                {
                    if (otherE == selfE) continue;
                    FPVector3 d = other->Pos - self->Pos; d.Y = 0;
                    FP dist = d.Magnitude;
                    if (dist >= MinDist) continue;
                    // Titik (hampir) sama: arah tak terdefinisi → dorong di sumbu X menurut indeks entity (deterministik,
                    // kedua pihak ke arah berlawanan) agar tidak menumpuk permanen.
                    FPVector3 n = dist <= FP._0_10
                        ? new FPVector3(selfE.Index < otherE.Index ? 1 : -1, 0, 0)
                        : d.Normalized;                                  // arah dari self ke other
                    FP overlap = MinDist - dist;
                    self->Pos -= n * (overlap * FP._0_50);              // self menjauh setengah tumpang-tindih
                }
            }
        }
    }
}
