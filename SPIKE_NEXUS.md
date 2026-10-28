# Spike Nexus (Quantum 3) — di atas fondasi photon-backend

File Nexus **terpisah penuh** dari Pong (folder sendiri). Pong **tidak disentuh** — tetap jadi referensi yang berfungsi.

## Yang sudah ditambahkan (v0, simulasi deterministik)
```
game/Assets/QuantumUser/Simulation/Nexus/Nexus.qtn          data contract (input, komponen NxCombat, global)
game/Assets/QuantumUser/Simulation/Nexus/NxCombatSystem.cs  spawn + gerak 8 arah + physics + timer
game/Assets/QuantumUser/Simulation/Nexus/NxHitSystem.cs     hit sederhana (sphere) → Damage Gate + hitstun/hitpause
game/Assets/QuantumUser/View/Nexus/NexusInput.cs            keyboard/touch → input (WASD + Space=Attack)
```
Semua di bawah asmref `Simulation/Quantum.Simulation.User.asmref` dan `View/Quantum.Unity.User.asmref` yang sudah ada → otomatis ikut ter-compile.

## Status: ✅ CodeGen sukses, simulasi compile bersih (hanya warning milik SDK).

## File tambahan untuk LOCAL PLAY (tekan Play, lihat kapsul — tanpa Photon/backend)
```
game/Assets/QuantumUser/View/Nexus/NexusView.cs   render kapsul 3D per entity + HUD HP
game/Assets/Editor/NexusSetup.cs                   buat scene local-play (debug runner + Nexus systems)
```
NxCombatSystem.OnInit juga spawn **1 dummy** (team 1) sebagai target uji pukulan.

## Cara jalankan (di Unity)
1. Biarkan Unity recompile (otomatis; file baru = C# biasa, **tidak** perlu CodeGen lagi — .qtn tidak berubah).
2. Menu **Nexus → Create Local Spike Scene** (membuat `Assets/Scenes/NexusLocal.unity`).
3. Tekan **Play**.
   - **WASD / panah** = gerak 8 arah. **Space / J** = pukul.
   - Kapsul **biru** = kamu, **oranye** = dummy. Pukul dummy → kedip merah + HP turun.
4. Kalau kapsul "kamu" tidak muncul (hanya dummy): local debug runner belum menambah player. Buka GameObject runner di scene → set **LocalPlayerCount = 1** (atau 2) → Play lagi. Kabari saya bila perlu.

## Fase 2 — 2 HERO NYATA (v2, aktif)
Sudah ditambah:
```
Simulation/Nexus/NxDamage.cs            logika damage BERSAMA (Guard/Critical/Gear/KO) — dipakai melee & projectile
Simulation/Nexus/NxProjectileSystem.cs projectile PULSE-X (gerak lurus, kena → NxDamage, hancur)
Nexus.qtn                               + NxCombat.HeroId/SkillCd/ShotCd, + component NxProjectile
```
- **TITAN-01** (HeroId 0, grappler): J = melee pendek keras · K = **lunge** (dash + melee gap-closer).
- **PULSE-X** (HeroId 1, zoner): J = **tembak** cepat · K = **charged shot** (lebih sakit). Dummy = PULSE-X (nembakin kamu).

**PENTING saat update ini:** `Nexus.qtn` BERUBAH (ada komponen baru) →
1. **Tools → Quantum → CodeGen → Run Qtn CodeGen** (generate QPrototypeNxProjectile.cs).
2. Re-run **Nexus → Create Local Spike Scene** (regen SystemsConfig agar NxProjectileSystem terdaftar).
3. Play. Kontrol: **WASD** gerak · **J** pukul/tembak · **K** skill · **Space** lompat · **Shift** dash · **L** guard · **H** ganti hero.

## Berikutnya (Fase 2 lanjutan)
- Hitbox dari animasi (bone-attached OBB baked), uji 10 pemain, backend meta (auth/MMR/matchmaking), aset 3D, split-session, anti-cheat replay.

## Prinsip yang dijaga (doc 20 revisi v2)
- Deterministik penuh (FP, tanpa float/RNG) → aman untuk Quantum + backend replay re-sim.
- Pong tetap utuh; Nexus di folder sendiri → tidak acak-acakan.
- Tumbuh bertahap: v0 (gerak + 1 pukulan) → Guard 180 → Critical 3 Break Stock + Gear Drop → OBB per frame (baked) → 10 pemain → lag 150ms → replay verify.

## Catatan
- Repo ini **privat** (SDK Quantum proprietary) — jangan commit SDK ke repo publik Nexus.
- Saya **tidak** menjalankan git di repo ini; silakan review & commit sendiri.
