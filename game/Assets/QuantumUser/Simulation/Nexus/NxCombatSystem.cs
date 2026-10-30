using Photon.Deterministic;

namespace Quantum
{
    /// <summary>
    /// Nexus brawler (SPIKE v2): gerak 8 arah, lompat, dash (i-frame), guard 180 lock-facing,
    /// faint, physics, timer, + 2 HERO: TITAN-01 (grappler/melee, skill=lunge gap-closer) dan
    /// PULSE-X (zoner/ranged, J=tembak, skill=charged shot). Ganti hero: tombol H (bit 32).
    /// Hit melee di NxHitSystem, projectile di NxProjectileSystem, damage bersama di NxDamage.
    /// Deterministik — tanpa float, tanpa RNG non-seeded.
    /// </summary>
    public unsafe class NxCombatSystem : SystemMainThreadFilter<NxCombatSystem.Filter>, ISignalOnPlayerAdded
    {
        public struct Filter { public EntityRef Entity; public NxCombat* C; }

        public static readonly FP MaxHp = 100;
        private static readonly FP MoveSpeed = 5;
        private static readonly FP JumpSpeed = 8;
        private static readonly FP DashSpeed = 17;     // dinaikkan: nutup jarak lebih jauh (counterplay vs zoner)
        private static readonly FP Gravity = FP._0_25 + FP._0_25 + FP._0_10; // ~0.6/tick
        private static readonly FP Diag = FP._0_50 + FP._0_20 + FP._0_01;    // ~0.71
        private const int AttackActiveTicks = 6;
        private const int DashTicks = 10;
        private const int DashCooldown = 24;           // diturunkan: dash lebih sering untuk mendekat
        private const int LungeDashTicks = 15;         // TITAN skill (K): dash gap-closer panjang

        // Hero: PULSE-X tembakan (di-nerf agar tidak stunlock dari jarak).
        private static readonly FP PulseShotSpeed = 15;
        private static readonly FP PulseShotDmg = 9;
        private const int PulseShotLife = 44;
        private const int PulseShotCd = 24;            // ~2.5 tembakan/detik (dulu 5) → ada celah untuk maju
        private static readonly FP PulseHeavySpeed = 20;
        private static readonly FP PulseHeavyDmg = 18;
        private const int PulseHeavyLife = 54;
        private const int SkillCooldown = 48;          // TITAN lunge cd
        private const int PulseHeavyCd = 72;           // charged shot cd (lebih lama karena burst)
        private const int TeamSize = 5;                // 5v5 = 10 fighter (uji skala rollback + bandwidth)
        private const int CritBleedTicks = 240;        // 4 dtk di Critical tanpa tertolong → auto-KO/respawn
        public const int KoTarget = 5;                 // tim pertama capai KO ini → match selesai (ranked). Samakan dgn backend Game:WinScore.

        public override void OnInit(Frame f)
        {
            f.Global->State = 0; // 0 = layar Pilih Hero. Bot di-spawn saat match MULAI (tahu jumlah manusia & mode).
        }

        public void OnPlayerAdded(Frame f, PlayerRef player, bool firstTime)
        {
            int slot = (int)player;
            f.Global->JoinedMask |= 1 << slot;
            var e = f.Create();
            // Pemain mulai sebagai TITAN-01 (HeroId 0); tekan H untuk coba PULSE-X.
            f.Set(e, NewFighter(player, (byte)(slot & 1), (slot & 1) == 0 ? new FPVector3(-3, 0, 0) : new FPVector3(3, 0, 0)));
        }

        public static NxCombat NewFighter(PlayerRef player, byte team, FPVector3 pos)
        {
            return new NxCombat
            {
                Player = player, Team = team, Posture = 0, Hp = MaxHp, GearMask = 0xF,
                GuardGauge = 100, Pos = pos, Yaw = team == 0 ? 0 : FP.Pi, HeroId = 0,
            };
        }

        // Spawn bot saat match MULAI, isi tiap tim sampai perTeam (dari f.PlayerCount — parameter sesi
        // Quantum yang PASTI tersinkron, bukan field custom). RANKED set PlayerCount=2 → perTeam=1 → 1v1
        // (2 manusia = TANPA bot; 1 manusia = +1 bot). BATTLE set PlayerCount=10 → perTeam=5 → 5v5 isi bot.
        private static void StartMatch(Frame f)
        {
            int h0 = 0, h1 = 0;
            foreach (var (e, o) in f.Unsafe.GetComponentBlockIterator<NxCombat>())
                if (o->Player != PlayerRef.None) { if (o->Team == 0) h0++; else h1++; }

            int target = f.PlayerCount;                 // total petarung yang diinginkan sesi ini
            int perTeam = target <= 2 ? 1 : (target + 1) / 2;
            if (perTeam > TeamSize) perTeam = TeamSize;

            for (int i = h0; i < perTeam; i++) SpawnBot(f, 0, i);
            for (int i = h1; i < perTeam; i++) SpawnBot(f, 1, i);

            f.Global->State = 1;
        }

        private static void SpawnBot(Frame f, byte team, int index)
        {
            FP x = team == 0 ? -6 : 6;
            FP z = (index - (TeamSize - 1) * FP._0_50) * 2; // sebar di sumbu Z
            var e = f.Create();
            var fighter = NewFighter(PlayerRef.None, team, new FPVector3(x, 0, z));
            fighter.HeroId = (byte)(index % 2); // selang-seling TITAN(0)/PULSE(1)
            fighter.AiTimer = (ushort)(index * 11); // offset biar tidak nembak serempak
            f.Set(e, fighter);
        }

        public override void Update(Frame f, ref Filter filter)
        {
            var c = filter.C;
            if (f.Global->State == 0) { HeroSelect(f, c); return; } // layar pilih hero
            if (f.Global->State != 1) return;

            // Statistik deterministik (docs/32): waktu bermain per hero + defisit KO terbesar.
            if (c->HeroId == 0) c->H0Ticks++; else c->H1Ticks++;
            int deficit = c->Team == 0 ? f.Global->ScoreB - f.Global->ScoreA : f.Global->ScoreA - f.Global->ScoreB;
            if (deficit > c->MaxDeficit) c->MaxDeficit = (byte)deficit;

            if (c->HitPause > 0) { c->HitPause--; return; }   // hitpause membekukan semua

            // Bleed-out: kalau nyangkut di Critical tanpa dihabisi, auto-KO → respawn (jadi SELALU balik).
            if (c->Critical != 0)
            {
                c->CritTimer++;
                if (c->CritTimer >= CritBleedTicks)
                {
                    // Bleed-out = KO oleh penyerang TERAKHIR (beri kill + skor), bukan "mati tanpa pembunuh".
                    if (c->LastHitValid != 0)
                        NxDamage.Ko(f, c->LastHitByTeam, c->LastHitByPlayer, filter.Entity, c);
                    else
                    {
                        c->Kos++; c->CurStreak = 0;
                        if (c->Team == 0) f.Global->ScoreB++; else f.Global->ScoreA++;
                        NxDamage.Respawn(f, c);
                    }
                    return;
                }
            }

            if (c->Faint > 0) { c->Faint--; c->Vel.X = 0; c->Vel.Z = 0; Physics(f, c); return; }

            bool controllable = c->HitStun == 0 && c->AttackTime == 0 && c->DashTime == 0;

            if (c->Player != PlayerRef.None)
            {
                var input = f.GetPlayerInput((int)c->Player);
                HandleInput(f, c, input, controllable);
            }
            else
            {
                DummyAi(f, c, controllable);
            }

            Physics(f, c);
            Timers(c);
        }

        // Layar Pilih Hero: pemain kirim pilihan via input (PickHero). Match mulai saat SEMUA pemain sudah pilih.
        private static void HeroSelect(Frame f, NxCombat* c)
        {
            if (c->Player == PlayerRef.None) return; // dummy menunggu
            if (c->Picked == 0)
            {
                var input = f.GetPlayerInput((int)c->Player);
                if (input != null)
                {
                    if (input->PickHero == 1) { c->HeroId = 0; c->Picked = 1; }
                    else if (input->PickHero == 2) { c->HeroId = 1; c->Picked = 1; }
                }
            }

            // Mulai bila semua entity pemain (bukan dummy) sudah memilih.
            bool allPicked = true; int count = 0;
            foreach (var (e, o) in f.Unsafe.GetComponentBlockIterator<NxCombat>())
            {
                if (o->Player == PlayerRef.None) continue;
                count++;
                if (o->Picked == 0) allPicked = false;
            }
            if (count > 0 && allPicked) StartMatch(f); // spawn bot sesuai mode + State→1 (sekali)
        }

        private static void HandleInput(Frame f, NxCombat* c, Input* input, bool controllable)
        {
            if (input == null) { c->Vel.X = 0; c->Vel.Z = 0; return; }

            // Guard (lock facing). Hanya di tanah.
            bool holdGuard = (input->Buttons & 16) != 0 && c->Posture == 0;
            if (holdGuard) { if (c->Guarding == 0) { c->Guarding = 1; c->GuardYaw = c->Yaw; } c->Vel.X = 0; c->Vel.Z = 0; return; }
            if (c->Guarding == 1) c->Guarding = 0;

            if (!controllable) return;

            // Ganti hero (H) — debounce pakai SkillCd.
            if ((input->Buttons & 32) != 0 && c->SkillCd == 0)
            {
                c->HeroId = (byte)(c->HeroId ^ 1);
                c->HeroSwitches++;
                c->SkillCd = 20; c->ShotCd = 0; c->AttackTime = 0;
                return;
            }

            // Skill (K) — hero-spesifik.
            if ((input->Buttons & 2) != 0 && c->SkillCd == 0 && c->Posture == 0)
            {
                c->SkillsUsed++;
                if (c->HeroId == 1)
                {
                    // PULSE-X: charged shot (lebih kencang & sakit).
                    FireProjectile(f, c, PulseHeavySpeed, PulseHeavyDmg, PulseHeavyLife, 1);
                    c->SkillCd = (ushort)PulseHeavyCd;
                }
                else
                {
                    // TITAN-01: lunge gap-closer panjang + armor (i-frame tembus peluru saat menyerbu).
                    c->DashTime = (ushort)LungeDashTicks; c->Invuln = (byte)LungeDashTicks;
                    c->AttackTime = (ushort)(AttackActiveTicks + 6); c->SkillSwing = 1;
                    c->SkillCd = (ushort)SkillCooldown;
                }
                return;
            }

            // Dash (i-frame) — ke arah hadap.
            if ((input->Buttons & 8) != 0 && c->DashCd == 0 && c->Posture == 0)
            {
                c->DashTime = DashTicks; c->DashCd = DashCooldown; c->Invuln = DashTicks;
                return;
            }
            // Jump.
            if ((input->Buttons & 4) != 0 && c->Posture == 0) { c->Vel.Y = JumpSpeed; c->Posture = 1; }
            // Attack dasar (J) — hero-spesifik.
            if ((input->Buttons & 1) != 0)
            {
                if (c->HeroId == 1)
                {
                    // PULSE-X: tembak bila siap → diam 1 tick. Saat cooldown JANGAN kunci gerak (bisa kiting).
                    if (c->ShotCd == 0) { FireProjectile(f, c, PulseShotSpeed, PulseShotDmg, PulseShotLife, 0); c->ShotCd = (ushort)PulseShotCd; return; }
                }
                else
                {
                    c->AttackTime = (ushort)AttackActiveTicks; c->SkillSwing = 0; // TITAN: ayunan melee normal
                    return;
                }
            }
            // Move (termasuk PULSE saat shot cooldown).
            Move(c, input->MoveDir);
        }

        private static void FireProjectile(Frame f, NxCombat* c, FP speed, FP damage, int life, byte isSkill)
        {
            FPVector3 fwd = new FPVector3(FPMath.Sin(c->Yaw), 0, FPMath.Cos(c->Yaw));
            var e = f.Create();
            f.Set(e, new NxProjectile
            {
                Team = c->Team,
                Owner = c->Player,
                Pos = c->Pos + fwd + new FPVector3(0, 1, 0), // muncul 1 unit di depan, setinggi dada
                Vel = fwd * speed,
                Life = (ushort)life,
                Damage = damage,
                IsSkill = isSkill,
            });
        }

        private static void DummyAi(Frame f, NxCombat* c, bool controllable)
        {
            if (!controllable) return;
            FPVector3 tgt; bool found = NearestEnemyPos(f, c, out tgt);
            if (!found) return;

            FPVector3 to = tgt - c->Pos; to.Y = 0;
            if (to.SqrMagnitude > FP._0_01) c->Yaw = FPMath.Atan2(to.X, to.Z);
            FP range2 = to.SqrMagnitude;
            c->AiTimer++;

            if (c->HeroId == 1)
            {
                // Zoner: jaga jarak menengah, tembak berkala.
                FPVector3 n = to.SqrMagnitude > FP._0_01 ? to.Normalized : new FPVector3(0, 0, 1);
                if (range2 < 12) { c->Vel.X = -n.X * MoveSpeed / 2; c->Vel.Z = -n.Z * MoveSpeed / 2; }        // hanya mundur kalau SANGAT dekat
                else if (range2 > 52) { c->Vel.X = n.X * MoveSpeed / 2; c->Vel.Z = n.Z * MoveSpeed / 2; }     // terlalu jauh → maju
                else { c->Vel.X = 0; c->Vel.Z = 0; }
                if (c->AiTimer > 40 && c->ShotCd == 0) { FireProjectile(f, c, PulseShotSpeed, PulseShotDmg, PulseShotLife, 0); c->ShotCd = (ushort)PulseShotCd; c->AiTimer = 0; }
            }
            else
            {
                // Grappler: mendekat lalu melee.
                if (range2 > 4) { FPVector3 n = to.Normalized; c->Vel.X = n.X * MoveSpeed / 2; c->Vel.Z = n.Z * MoveSpeed / 2; }
                else { c->Vel.X = 0; c->Vel.Z = 0; }
                if (range2 < 6 && c->AiTimer > 90) { c->AttackTime = (ushort)AttackActiveTicks; c->SkillSwing = 0; c->AiTimer = 0; }
            }
        }

        private static bool NearestEnemyPos(Frame f, NxCombat* self, out FPVector3 pos)
        {
            pos = default; FP best = FP.UseableMax; bool ok = false;
            foreach (var (e, o) in f.Unsafe.GetComponentBlockIterator<NxCombat>())
            {
                if (o->Team == self->Team) continue;
                FP d = (o->Pos - self->Pos).SqrMagnitude;
                if (d < best) { best = d; pos = o->Pos; ok = true; }
            }
            return ok;
        }

        private static void Move(NxCombat* c, byte dir)
        {
            if (dir == 0) { c->Vel.X = 0; c->Vel.Z = 0; return; }
            FPVector2 d = Dir8(dir);
            c->Vel.X = d.X * MoveSpeed; c->Vel.Z = d.Y * MoveSpeed;
            c->Yaw = FPMath.Atan2(d.X, d.Y);
        }

        private static void Physics(Frame f, NxCombat* c)
        {
            if (c->DashTime > 0)
            {
                FPVector3 fwd = new FPVector3(FPMath.Sin(c->Yaw), 0, FPMath.Cos(c->Yaw));
                c->Vel.X = fwd.X * DashSpeed; c->Vel.Z = fwd.Z * DashSpeed;
            }
            if (c->Posture == 1) c->Vel.Y -= Gravity;
            c->Pos += c->Vel * f.DeltaTime;
            if (c->Pos.Y <= 0) { c->Pos.Y = 0; if (c->Vel.Y < 0) c->Vel.Y = 0; if (c->Posture == 1) c->Posture = 0; }
        }

        private static void Timers(NxCombat* c)
        {
            if (c->HitStun > 0) c->HitStun--;
            if (c->AttackTime > 0) c->AttackTime--;
            if (c->DashTime > 0) c->DashTime--;
            if (c->DashCd > 0) c->DashCd--;
            if (c->Invuln > 0) c->Invuln--;
            if (c->SkillCd > 0) c->SkillCd--;
            if (c->ShotCd > 0) c->ShotCd--;
            // Guard gauge: regen saat tidak guard.
            if (c->Guarding == 0 && c->GuardGauge < 100) c->GuardGauge = (byte)(c->GuardGauge + 1 > 100 ? 100 : c->GuardGauge + 1);
        }

        private static FPVector2 Dir8(byte d)
        {
            FP s = Diag;
            switch (d)
            {
                case 1: return new FPVector2(0, 1);
                case 2: return new FPVector2(s, s);
                case 3: return new FPVector2(1, 0);
                case 4: return new FPVector2(s, -s);
                case 5: return new FPVector2(0, -1);
                case 6: return new FPVector2(-s, -s);
                case 7: return new FPVector2(-1, 0);
                case 8: return new FPVector2(-s, s);
                default: return default;
            }
        }
    }
}
