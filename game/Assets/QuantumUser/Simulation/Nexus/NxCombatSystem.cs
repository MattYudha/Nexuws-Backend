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
        // T2: bleed-out DIHAPUS. Nilai T2 ada di NxTuning.cs (satu tempat).
        public const int KoTarget = 5;                 // tim pertama capai KO ini → match selesai (ranked). Samakan dgn backend Game:WinScore.

        public override void OnInit(Frame f)
        {
            f.Global->State = 0; // 0 = layar Pilih Hero. Bot di-spawn saat match MULAI (tahu jumlah manusia & mode).
            f.Global->WinnerTeam = NxMatch.Draw; // belum ada pemenang (bukan "tim 0")
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

            int target = f.MaxPlayerCount;              // sesi: RANKED=2, PRACTICE=3, BATTLE=10 (MaxPlayerCount: PlayerCount usang)
            int perTeam = target >= 6 ? 5 : 1;          // battle=5v5; ranked/practice=1v1
            if (perTeam > TeamSize) perTeam = TeamSize;
            bool ranked = target == 2;                  // RANKED = murni manusia, TANPA bot (user 2026-11-16)

            if (!ranked)
            {
                for (int i = h0; i < perTeam; i++) SpawnBot(f, 0, i);
                for (int i = h1; i < perTeam; i++) SpawnBot(f, 1, i);
            }

            f.Global->State = 1;
            NxMatch.Begin(f); // T2: timer 100 dtk + koTimeline kosong
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

            // T2: AFK (hanya pemain manusia). Guard/serang/dash juga dihitung "aktif".
            TrackActivity(f, c);
            if (f.Global->State != 1) return;   // forfeit bisa mengakhiri match di sini

            if (c->HitPause > 0) { c->HitPause--; return; }   // hitpause membekukan semua

            // T2: Critical hanya selesai lewat 3 Break Stock. Bleed-out DIHAPUS.
            UpdateCriticalPressure(c);

            // Overload: terkunci 1,5 dtk, tidak bisa bergerak/bertindak, BUKAN KO.
            if (c->OverloadLock > 0)
            {
                c->OverloadLock--; c->Vel.X = 0; c->Vel.Z = 0;
                Physics(f, c); Timers(c); return;
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

        /// <summary>T2: AFK. Reset oleh gerak/lompat/guard/dash/serang/skill. Mouse tidak dihitung.
        /// Ambang (NxTuning): 10 dtk peringatan — View membaca IdleTicks; 20 dtk berturut-turut kalah WO.
        /// Disconnect = input kosong → jalur yang sama.</summary>
        private static void TrackActivity(Frame f, NxCombat* c)
        {
            if (c->Player == PlayerRef.None) return;              // bot tidak pernah AFK
            var input = f.GetPlayerInput((int)c->Player);
            bool acted = input != null && (input->MoveDir != 0 || (input->Buttons & NxTuning.ActionButtons) != 0);
            if (acted) { c->IdleTicks = 0; return; }
            if (c->IdleTicks < ushort.MaxValue) c->IdleTicks++;
            if (c->IdleTicks < NxTuning.AfkForfeitTicks) return;

            // WO: lawan menang, skor TIDAK diubah (backend memvalidasi kills = skor).
            NxMatch.End(f, NxMatch.ReasonForfeit, (byte)(c->Team ^ 1));
        }

        /// <summary>T2: tekanan saat Critical. Tidak ada damage tambahan — hanya lock (Overload).
        /// Dipanggil setiap tick; timer di-reset oleh NxDamage saat korban kena pukul.</summary>
        private static void UpdateCriticalPressure(NxCombat* c)
        {
            if (c->Critical == 0) { c->OverloadCharge = 0; c->OverloadLock = 0; return; }
            if (c->OverloadLock > 0) return;                       // decrement dilakukan di Update
            if (c->OverloadCharge < ushort.MaxValue) c->OverloadCharge++;
            if (c->OverloadCharge >= NxTuning.OverloadTriggerTicks)
            {
                c->OverloadCharge = 0;
                c->OverloadLock = (byte)NxTuning.OverloadLockTicks;
                c->Guarding = 0; // terkunci = rentan: guard ikut lepas
            }
        }

        /// <summary>T2/C4: 70% sejak masuk Critical, 50% di stock 2, 35% di stock 1.</summary>
        public static FP CritSpeedMul(NxCombat* c)
        {
            if (c->Critical == 0) return FP._1;
            int pm = c->BreakStock >= 3 ? NxTuning.CritSpeedPermilleStock3
                   : c->BreakStock == 2 ? NxTuning.CritSpeedPermilleStock2
                   : NxTuning.CritSpeedPermilleStock1;
            return (FP)pm / 1000;
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

            // Skill (K) — hero-spesifik. T2/C4: lunge TITAN = dash → ikut dikunci saat Critical.
            if ((input->Buttons & 2) != 0 && c->SkillCd == 0 && c->Posture == 0 && (c->HeroId == 1 || c->Critical == 0))
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

            // Dash (i-frame) — ke arah hadap. T2: dikunci selama Critical (C4).
            if ((input->Buttons & 8) != 0 && c->DashCd == 0 && c->Posture == 0 && c->Critical == 0)
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
            // Aim assist (PULSE): arahkan peluru ke musuh terdekat DI DEPAN (jarak+cone). Karakter TIDAK berputar; peluru tetap lurus.
            FP bestD = NxTuning.AimAssistRange * NxTuning.AimAssistRange; FPVector3 aim = fwd; bool found = false;
            foreach (var (ve, vo) in f.Unsafe.GetComponentBlockIterator<NxCombat>())
            {
                if (vo->Team == c->Team) continue;
                FPVector3 to = vo->Pos - c->Pos; to.Y = 0;
                FP d2 = to.SqrMagnitude;
                if (d2 <= FP._0_10 || d2 > bestD) continue;
                FPVector3 dir = to.Normalized;
                if (FPVector3.Dot(fwd, dir) < NxTuning.AimAssistMinDot) continue; // hanya musuh di depan
                bestD = d2; aim = dir; found = true;
            }
            if (found) fwd = aim;
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
            FPVector3 tgt; bool found = BestTarget(f, c, out tgt);
            if (!found) return;

            FPVector3 to = tgt - c->Pos; to.Y = 0;
            if (to.SqrMagnitude > FP._0_01) c->Yaw = FPMath.Atan2(to.X, to.Z);
            FP range2 = to.SqrMagnitude;
            FP mul = CritSpeedMul(c);                       // T2/C4: bot juga kena penalti
            c->AiTimer++;

            if (c->HeroId == 1)
            {
                // Zoner: jaga jarak menengah, tembak berkala.
                FPVector3 n = to.SqrMagnitude > FP._0_01 ? to.Normalized : new FPVector3(0, 0, 1);
                if (range2 < 12) { c->Vel.X = -n.X * MoveSpeed * mul / 2; c->Vel.Z = -n.Z * MoveSpeed * mul / 2; }        // hanya mundur kalau SANGAT dekat
                else if (range2 > 52) { c->Vel.X = n.X * MoveSpeed * mul / 2; c->Vel.Z = n.Z * MoveSpeed * mul / 2; }     // terlalu jauh → maju
                else { c->Vel.X = 0; c->Vel.Z = 0; }
                if (c->AiTimer > 40 && c->ShotCd == 0) { FireProjectile(f, c, PulseShotSpeed, PulseShotDmg, PulseShotLife, 0); c->ShotCd = (ushort)PulseShotCd; c->AiTimer = 0; }
            }
            else
            {
                // Grappler: mendekat lalu melee.
                if (range2 > 4) { FPVector3 n = to.Normalized; c->Vel.X = n.X * MoveSpeed * mul / 2; c->Vel.Z = n.Z * MoveSpeed * mul / 2; }
                else { c->Vel.X = 0; c->Vel.Z = 0; }
                if (range2 < 6 && c->AiTimer > 90) { c->AttackTime = (ushort)AttackActiveTicks; c->SkillSwing = 0; c->AiTimer = 0; }
            }
        }

        /// <summary>T2: bot memilih skor terendah = jarak^2 − bonus Critical (−40) / Overload (−80)
        /// + penalti penumpukan (+30 per kawan yang sudah dekat target). Seri → EntityRef.Index terkecil.</summary>
        private static bool BestTarget(Frame f, NxCombat* self, out FPVector3 pos)
        {
            pos = default; FP best = FP.UseableMax; int bestIdx = int.MaxValue; bool ok = false;
            FP crowdR2 = (FP)NxTuning.BotCrowdRadius * NxTuning.BotCrowdRadius;
            foreach (var (e, o) in f.Unsafe.GetComponentBlockIterator<NxCombat>())
            {
                if (o == self || o->Team == self->Team) continue;
                FP score = (o->Pos - self->Pos).SqrMagnitude;
                if (o->Critical != 0) score -= NxTuning.BotCriticalBonus;
                if (o->OverloadLock > 0) score -= NxTuning.BotOverloadBonus;
                int crowd = 0;
                foreach (var (e2, o2) in f.Unsafe.GetComponentBlockIterator<NxCombat>())
                {
                    if (o2 == o || o2 == self || o2->Team != self->Team) continue; // kawan LAIN di dekat target
                    if ((o2->Pos - o->Pos).SqrMagnitude < crowdR2) crowd++;
                }
                score += crowd * NxTuning.BotCrowdPenalty;
                if (score < best || (score == best && e.Index < bestIdx)) { best = score; bestIdx = e.Index; pos = o->Pos; ok = true; }
            }
            return ok;
        }

        private static void Move(NxCombat* c, byte dir)
        {
            if (dir == 0) { c->Vel.X = 0; c->Vel.Z = 0; return; }
            FPVector2 d = Dir8(dir);
            FP mul = CritSpeedMul(c);                       // T2/C4: 70/50/35% saat Critical
            c->Vel.X = d.X * MoveSpeed * mul; c->Vel.Z = d.Y * MoveSpeed * mul;
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
