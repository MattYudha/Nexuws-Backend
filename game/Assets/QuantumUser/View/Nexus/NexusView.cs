using System.Collections.Generic;
using Quantum;
using UnityEngine;

namespace Nexus
{
    /// <summary>
    /// Render kapsul 3D per entity NxCombat dari predicted frame (SPIKE v0, local play).
    /// Tanpa prefab/view-asset: buat GameObject kapsul primitif saat entity muncul,
    /// update posisi + warna (team), hapus saat entity hilang. Kamera quarter-view sederhana.
    /// </summary>
    public unsafe class NexusView : MonoBehaviour
    {
        private readonly Dictionary<EntityRef, GameObject> _caps = new();
        private readonly Dictionary<EntityRef, GameObject> _projs = new();
        private Camera _camera;
        private static Texture2D _portTitan, _portPulse; private static bool _portTried; // NX: potret hero
        private static Texture2D Portrait(int pick)
        {
            if (!_portTried) { _portTried = true; _portTitan = Resources.Load<Texture2D>("hero_titan"); _portPulse = Resources.Load<Texture2D>("hero_pulse"); }
            return pick == 1 ? _portTitan : _portPulse;
        }
        private static void HeroCard(float x, float y, int pick, string name, string role, string[] skills)
        {
            GUI.Box(new Rect(x, y, 230, 300), "");
            var tex = Portrait(pick);
            var pr = new Rect(x + 40, y + 8, 150, 150);
            if (tex != null) GUI.DrawTexture(pr, tex, ScaleMode.ScaleToFit);
            else { var pb = GUI.backgroundColor; GUI.backgroundColor = new Color(0.25f,0.45f,0.6f); GUI.Box(pr, name); GUI.backgroundColor = pb; }
            var nm = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            var rl = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            var sk = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            GUI.Label(new Rect(x + 10, y + 158, 210, 22), name, nm);
            GUI.Label(new Rect(x + 10, y + 180, 210, 18), role, rl);
            for (int i = 0; i < skills.Length; i++) GUI.Label(new Rect(x + 14, y + 202 + i * 17, 206, 16), skills[i], sk);
            if (GUI.Button(new Rect(x + 40, y + 268, 150, 26), "PILIH")) NexusInput.PendingPick = (byte)pick;
        }
        private readonly HashSet<EntityRef> _seen = new();
        private readonly HashSet<EntityRef> _seenProj = new();

        private void Awake()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                _camera = go.AddComponent<Camera>();
            }
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.05f, 0.06f, 0.1f);
            // Quarter-view terkunci: di atas-belakang, melihat ke origin.
            _camera.transform.position = new Vector3(0, 12, -11);
            _camera.transform.rotation = Quaternion.Euler(45, 0, 0);

            // Lantai referensi (Unlit agar terlihat di build tanpa cahaya).
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(3, 1, 3);
            floor.GetComponent<Renderer>().material = UnlitMat(new Color(0.12f, 0.13f, 0.2f));
        }

        private static Shader _unlit;
        private static Material UnlitMat(Color c)
        {
            if (_unlit == null) _unlit = Shader.Find("Unlit/Color");
            if (_unlit == null) _unlit = Shader.Find("Standard");
            var m = new Material(_unlit);
            m.color = c;
            return m;
        }

        private static Frame PredictedFrame()
        {
            var runner = QuantumRunner.Default;
            if (runner == null || runner.Game == null) return null;
            return runner.Game.Frames.Predicted;
        }

        private void Update()
        {
            var frame = PredictedFrame();
            if (frame == null) return;

            // Match selesai → bersihkan arena (fighter + projectile) agar background hasil bersih.
            if (frame.Global->State == 2) { ClearArena(); return; }

            _seen.Clear();
            foreach (var (entity, c) in frame.Unsafe.GetComponentBlockIterator<NxCombat>())
            {
                _seen.Add(entity);
                if (!_caps.TryGetValue(entity, out var go))
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    go.name = "Nx_" + entity;
                    go.GetComponent<Renderer>().material =
                        UnlitMat(c->Team == 0 ? new Color(0.3f, 0.8f, 1f) : new Color(1f, 0.5f, 0.3f));
                    _caps[entity] = go;
                }

                var pos = new Vector3(c->Pos.X.AsFloat, c->Pos.Y.AsFloat + 1f, c->Pos.Z.AsFloat);
                go.transform.position = pos;
                go.transform.rotation = Quaternion.Euler(0, c->Yaw.AsFloat * Mathf.Rad2Deg, 0); // sim: hadap = (sin yaw, cos yaw) = konvensi Unity

                // Warna status: Critical=ungu, hitstun/hit=merah, faint=kuning, guard=hijau, normal=team.
                var r = go.GetComponent<Renderer>();
                var baseCol = c->Team == 0 ? new Color(0.3f, 0.8f, 1f) : new Color(1f, 0.5f, 0.3f);
                if (c->Player != PlayerRef.None) baseCol = new Color(0.6f, 1f, 1f); // KAMU = cyan terang
                Color col = baseCol;
                if (c->Critical != 0) col = new Color(0.7f, 0.2f, 1f);
                if (c->Faint > 0) col = Color.yellow;
                else if (c->HitStun > 0) col = Color.red;
                else if (c->Guarding != 0) col = new Color(0.2f, 1f, 0.4f);
                r.material.color = col;
                // Kecilkan sedikit saat Down/Critical agar terbaca.
                go.transform.localScale = c->Critical != 0 ? new Vector3(1, 0.8f, 1) : Vector3.one;
            }

            // Hapus kapsul untuk entity yang sudah tidak ada.
            var toRemove = new List<EntityRef>();
            foreach (var kv in _caps) if (!_seen.Contains(kv.Key)) toRemove.Add(kv.Key);
            foreach (var k in toRemove) { Destroy(_caps[k]); _caps.Remove(k); }

            // ---- Projectile (PULSE-X): bola kecil ----
            _seenProj.Clear();
            foreach (var (entity, p) in frame.Unsafe.GetComponentBlockIterator<NxProjectile>())
            {
                _seenProj.Add(entity);
                if (!_projs.TryGetValue(entity, out var pg))
                {
                    pg = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    pg.name = "Proj_" + entity;
                    pg.transform.localScale = Vector3.one * 0.6f;
                    pg.GetComponent<Renderer>().material =
                        UnlitMat(p->Team == 0 ? new Color(0.5f, 0.95f, 1f) : new Color(1f, 0.85f, 0.3f));
                    _projs[entity] = pg;
                }
                _projs[entity].transform.position = new Vector3(p->Pos.X.AsFloat, p->Pos.Y.AsFloat, p->Pos.Z.AsFloat);
            }
            var projGone = new List<EntityRef>();
            foreach (var kv in _projs) if (!_seenProj.Contains(kv.Key)) projGone.Add(kv.Key);
            foreach (var k in projGone) { Destroy(_projs[k]); _projs.Remove(k); }
        }

        /// <summary>Hapus semua GameObject fighter + projectile (dipakai saat match selesai).</summary>
        private void ClearArena()
        {
            foreach (var kv in _caps) Destroy(kv.Value);
            _caps.Clear();
            foreach (var kv in _projs) Destroy(kv.Value);
            _projs.Clear();
        }

        private void OnGUI()
        {
            var frame = PredictedFrame();
            if (frame == null) return; // di lobby (belum ada sim) jangan gambar overlay debug yang menimpa UI

            // ---- Layar PILIH HERO (State 0) ----
            if (frame.Global->State == 0)
            {
                float sw = UnityEngine.Screen.width, sh = UnityEngine.Screen.height;
                var title = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                var center = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };

                // Kesiapan pemain manusia (sudah pilih atau belum).
                int humans = 0, ready = 0;
                foreach (var (e, c) in frame.Unsafe.GetComponentBlockIterator<NxCombat>())
                    if (c->Player != PlayerRef.None) { humans++; if (c->Picked != 0) ready++; }

                byte myPick = NexusInput.PendingPick;

                GUI.Box(new Rect(sw / 2 - 265, sh / 2 - 125, 530, 370), "");
                GUI.Label(new Rect(sw / 2 - 265, sh / 2 - 117, 530, 40), "PILIH HERO", title);

                if (myPick == 0)
                {
                    HeroCard(sw / 2 - 250, sh / 2 - 92, 1, "TITAN-01", "Grappler / Melee",
                        new[] { "J  Hantam", "K  Serbu (kebal peluru)", "Shift  Dash (kebal)", "L  Guard 180" });
                    HeroCard(sw / 2 + 20, sh / 2 - 92, 2, "PULSE-X", "Zoner / Ranged",
                        new[] { "J  Tembak", "K  Charged (tembus)", "Shift  Dash (kebal)", "L  Guard 180" });
                    GUI.Label(new Rect(sw / 2 - 250, sh / 2 + 212, 500, 22), "Klik PILIH di bawah hero.", center);
                }
                else
                {
                    // Sudah memilih → konfirmasi HIJAU + menunggu pemain lain.
                    string hero = myPick == 1 ? "TITAN-01 (Grappler)" : "PULSE-X (Zoner)";
                    var okStyle = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                    var prevBg = GUI.backgroundColor; var prevC = GUI.contentColor;
                    GUI.backgroundColor = new Color(0.2f, 0.8f, 0.35f); GUI.contentColor = Color.white;
                    GUI.Box(new Rect(sw / 2 - 160, sh / 2 - 55, 320, 80), "✓ " + hero + "\nTERPILIH", okStyle);
                    GUI.backgroundColor = prevBg; GUI.contentColor = prevC;

                    GUI.Label(new Rect(sw / 2 - 230, sh / 2 + 45, 460, 28),
                        humans >= 2 ? $"Menunggu pemain lain memilih...   ({ready}/{humans} siap)"
                                    : "Menunggu pemain lain bergabung...", center);
                }
                return;
            }

            // ---- MATCH SELESAI (State 2, ranked) ----
            if (frame.Global->State == 2)
            {
                float sw = UnityEngine.Screen.width;
                var big = new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                // T2: pemenang dari sim (tie-break waktu habis / forfeit), bukan tebakan dari skor.
                byte wt = frame.Global->WinnerTeam;
                string why = frame.Global->EndReason == NxMatch.ReasonTime ? "WAKTU HABIS" : frame.Global->EndReason == NxMatch.ReasonForfeit ? "AFK — WO" : "MATCH SELESAI";
                string res = wt == NxMatch.Draw ? "SERI" : (wt == 0 ? "TIM BIRU" : "TIM ORANYE") + " MENANG";
                GUI.Box(new Rect(sw / 2 - 260, 70, 520, 70), "");
                GUI.Label(new Rect(sw / 2 - 260, 80, 520, 50),
                    $"{why} — {res} {frame.Global->ScoreA}-{frame.Global->ScoreB}", big);
                return;
            }

            // ---- T2: timer, peringatan AFK, indikator Critical lawan ----
            NxCombat* me = null;
            var runner = QuantumRunner.Default;
            var locals = runner != null && runner.Game != null ? runner.Game.GetLocalPlayers() : null;
            if (locals != null && locals.Count > 0)
                foreach (var (e, c) in frame.Unsafe.GetComponentBlockIterator<NxCombat>())
                    if (c->Player == locals[0]) { me = c; break; }

            {
                float sw = UnityEngine.Screen.width;
                int secs = (frame.Global->TimeLeftTicks + NxTuning.TickRate - 1) / NxTuning.TickRate;
                var timer = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                timer.normal.textColor = secs <= 10 ? new Color(1f, 0.85f, 0.3f) : Color.white;
                GUI.Label(new Rect(sw / 2 - 60, 8, 120, 36), $"{secs / 60}:{secs % 60:00}", timer);

                if (me != null && me->IdleTicks >= NxTuning.AfkWarnTicks)
                {
                    int left = (NxTuning.AfkForfeitTicks - me->IdleTicks + NxTuning.TickRate - 1) / NxTuning.TickRate;
                    var warn = new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                    warn.normal.textColor = new Color(1f, 0.85f, 0.3f);
                    GUI.Box(new Rect(sw / 2 - 230, 50, 460, 40), $"TIDAK ADA INPUT — kalah WO dalam {Mathf.Max(0, left)} dtk", warn);
                }
            }

            if (me != null && _camera != null)
            {
                // Lawan di Critical: penanda di atas kepala, atau panah di tepi layar bila di luar pandangan (merah = lawan).
                var mark = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                mark.normal.textColor = new Color(0.94f, 0.43f, 0.43f);
                float sw = UnityEngine.Screen.width, sh = UnityEngine.Screen.height;
                foreach (var (e, c) in frame.Unsafe.GetComponentBlockIterator<NxCombat>())
                {
                    if (c->Team == me->Team || c->Critical == 0) continue;
                    string tag = c->OverloadLock > 0 ? $"OVERLOAD · CRIT {c->BreakStock}" : $"CRITICAL {c->BreakStock}";
                    Vector3 sp = _camera.WorldToScreenPoint(new Vector3(c->Pos.X.AsFloat, c->Pos.Y.AsFloat + 2.6f, c->Pos.Z.AsFloat));
                    bool behind = sp.z < 0;
                    float mx = sp.x, my = sh - sp.y;
                    if (behind) { mx = sw - mx; my = sh - my; }
                    bool off = behind || mx < 0 || mx > sw || my < 0 || my > sh;
                    if (off)
                    {
                        mx = Mathf.Clamp(mx, 40, sw - 40); my = Mathf.Clamp(my, 40, sh - 40);
                        string arrow = mx <= 40 ? "◄ " : mx >= sw - 40 ? " ►" : my <= 40 ? "▲ " : "▼ ";
                        tag = mx >= sw - 40 ? tag + arrow : arrow + tag;
                    }
                    else tag = "▼ " + tag;
                    GUI.Label(new Rect(mx - 110, my - 14, 220, 28), tag, mark);
                }
            }

            GUI.Label(new Rect(10, 10, 900, 24), "SPIKE Nexus 5v5 — WASD gerak · J pukul/tembak · K skill · Space lompat · Shift dash · L guard · H ganti hero.");
            int fps = (int)(1f / Mathf.Max(0.0001f, Time.smoothDeltaTime));
            var score = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(10, 30, 900, 26), $"TIM BIRU  {frame.Global->ScoreA}  :  {frame.Global->ScoreB}  TIM ORANYE   (menang di {NxCombatSystem.KoTarget} KO){(NexusSettings.ShowFps ? $"   |   FPS: {fps}" : "")}", score);

            float y = 54;
            foreach (var (entity, c) in frame.Unsafe.GetComponentBlockIterator<NxCombat>())
            {
                string who = c->Player != PlayerRef.None ? "YOU  " : (c->Team == 0 ? "ALLY " : "ENEMY");
                string hero = c->HeroId == 0 ? "TITAN-01" : "PULSE-X ";
                string st = c->Critical != 0 ? $"CRIT s={c->BreakStock}" : $"HP {c->Hp.AsFloat:0}";
                string extra = c->OverloadLock > 0 ? " OVERLOAD" : c->Faint > 0 ? " FAINT" : (c->Guarding != 0 ? " GUARD" : "");
                GUI.Label(new Rect(10, y, 520, 18), $"{who} [{hero}]  {st}{extra}");
                y += 18;
            }
        }
    }
}
