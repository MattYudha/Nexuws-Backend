using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NexusEditor
{
    /// <summary>
    /// Tahap 1 — generate sprite 9-slice (PNG) untuk lobby baru + PanelSettings PC + scene uji sprite.
    /// Menu: Nexus → Generate Lobby Sprites · Nexus → Setup Sprite Test Scene.
    /// Rounded ~9px, slice 12, gradient kontras, highlight 1px di tepi atas, bevel bawah.
    /// </summary>
    public static class NexusSpriteGen
    {
        private const string Dir = "Assets/NexusUI/Sprites";
        private const int Size = 48, Radius = 9, Border = 3, Slice = 12;
        private static readonly Color Clear = new Color(0, 0, 0, 0);

        [MenuItem("Nexus/Generate Lobby Sprites")]
        public static void Generate()
        {
            Directory.CreateDirectory(Dir);

            // Tombol aksi: gradient kontras + highlight + bevel.
            SaveBtn("btn_orange", C(242, 140, 30), C(255, 179, 71));
            SaveBtn("btn_green", C(60, 176, 67), C(110, 224, 122));
            SaveBtn("btn_blue", C(58, 123, 213), C(79, 209, 255));
            SaveBtn("btn_gray", C(92, 112, 150), C(150, 170, 205));
            SaveBtn("btn_red", C(214, 72, 68), C(242, 120, 112));

            // Mode (navy): normal border biru redup #2A4A7A, active border cyan terang.
            SaveFrame("btn_mode", C(20, 38, 72, 236), C(42, 74, 122, 200), true, Clear);
            SaveFrame("btn_mode_active", C(28, 54, 100, 246), C(79, 209, 255, 255), true, Clear);

            // Tab: inactive biru redup, active navy LEBIH TERANG + garis bawah cyan (bukan putih).
            SaveFrame("tab_inactive", C(20, 36, 70, 205), C(42, 74, 122, 160), false, Clear);
            SaveFrame("tab_active", C(36, 62, 110, 235), C(60, 96, 150, 220), false, C(79, 209, 255, 255));

            // Panel: navy transparan 0.85 + border biru redup #2A4A7A (cyan hanya elemen aktif).
            SaveFrame("panel_frame", C(15, 30, 60, 217), C(42, 74, 122, 255), false, Clear);

            // Depth: vignette background + strip gradient top bar / bottom nav.
            SaveVignette("lobby_vignette");
            SaveStrip("strip_top", true);
            SaveStrip("strip_bottom", false);

            // Ikon placeholder berbentuk (amplop / gear) — bukan huruf.
            SaveIconMail("ic_mail");
            SaveIconGear("ic_settings");

            AssetDatabase.Refresh();
            ConfigurePanelSettings();
            // Reimport USS supaya url(...) ke sprite baru ter-resolve (hindari ikon "missing").
            AssetDatabase.ImportAsset("Assets/NexusUI/NexusLobby.uss", ImportAssetOptions.ForceUpdate);
            Debug.Log("✓ Lobby sprites + vignette + ikon di " + Dir + ". USS di-reimport. PanelSettings 1920x1080.");
        }

        private static void SaveBtn(string name, Color baseCol, Color borderCol)
        {
            Color top = Color.Lerp(baseCol, Color.white, 0.52f);
            Color bot = Color.Lerp(baseCol, Color.black, 0.30f);
            Save(name, RoundRect(top, baseCol, bot, borderCol, gloss: true, bottom: Clear));
        }

        private static void SaveFrame(string name, Color fill, Color borderCol, bool gloss, Color bottom)
        {
            Color top = Color.Lerp(fill, Color.white, gloss ? 0.16f : 0.07f);
            Color bot = Color.Lerp(fill, Color.black, 0.14f);
            Save(name, RoundRect(top, fill, bot, borderCol, gloss, bottom));
        }

        private static Texture2D RoundRect(Color topCol, Color midCol, Color botCol, Color borderCol, bool gloss, Color bottom)
        {
            int n = Size; float c = n * 0.5f, half = n * 0.5f;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            bool useBottom = bottom.a > 0.01f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - c) - (half - Radius);
                    float dy = Mathf.Abs(y + 0.5f - c) - (half - Radius);
                    float outside = Mathf.Sqrt(Mathf.Max(dx, 0) * Mathf.Max(dx, 0) + Mathf.Max(dy, 0) * Mathf.Max(dy, 0));
                    float sdf = outside + Mathf.Min(Mathf.Max(dx, dy), 0) - Radius;
                    float aa = Mathf.Clamp01(0.5f - sdf);
                    if (aa <= 0f) { px[y * n + x] = Clear; continue; }

                    float vf = (float)y / (n - 1); // 0 bawah → 1 atas
                    Color fill = vf >= 0.5f ? Color.Lerp(midCol, topCol, (vf - 0.5f) * 2f) : Color.Lerp(botCol, midCol, vf * 2f);
                    if (gloss && vf > 0.60f) fill = Color.Lerp(fill, Color.white, 0.18f * (vf - 0.60f) / 0.40f); // sheen atas

                    float bm = Mathf.Clamp01(sdf + Border);
                    Color col = Color.Lerp(fill, borderCol, bm);

                    if (gloss && bm < 0.35f)
                    {
                        float topDist = (n - 1) - y, botDist = y;
                        if (topDist >= Border && topDist <= Border + 1.4f) col = Color.Lerp(col, Color.white, 0.6f);     // inner highlight 1px
                        if (botDist >= Border && botDist <= Border + 2.5f) col = Color.Lerp(col, Color.black, 0.18f);     // bevel bawah
                    }
                    if (useBottom && y <= 2) col = bottom; // garis aksen bawah (tab active)

                    col.a *= aa;
                    px[y * n + x] = col;
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        private static void SaveVignette(string name)
        {
            int n = 1024; // resolusi tinggi → gradient halus (Save: Bilinear + uncompressed)
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            Color center = C(24, 42, 76), edge = C(6, 11, 26);
            float cx = (n - 1) * 0.5f, cy = (n - 1) * 0.5f, maxd = Mathf.Sqrt(cx * cx + cy * cy);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / maxd;
                    px[y * n + x] = Color.Lerp(center, edge, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d * 1.15f)));
                }
            t.SetPixels(px); t.Apply();
            Save(name, t, 0);
        }

        private static void SaveStrip(string name, bool darkTop)
        {
            int w = 16, h = 128;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float vf = (float)y / (h - 1);               // 0 bawah → 1 atas (tekstur)
                float edge = darkTop ? vf : (1f - vf);        // sisi gelap
                Color col = C(8, 16, 36); col.a = Mathf.Lerp(0.14f, 0.92f, edge);
                for (int x = 0; x < w; x++) px[y * w + x] = col;
            }
            t.SetPixels(px); t.Apply();
            Save(name, t, 0);
        }

        // Rasterisasi segmen garis putih (anti-alias) untuk ikon placeholder.
        private static void Seg(Color[] px, int n, float ax, float ay, float bx, float by, float hw)
        {
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float wx = x + 0.5f - ax, wy = y + 0.5f - ay, vx = bx - ax, vy = by - ay;
                    float tt = Mathf.Clamp01((wx * vx + wy * vy) / (vx * vx + vy * vy));
                    float dx = wx - tt * vx, dy = wy - tt * vy;
                    float a = Mathf.Clamp01(hw + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                    if (a > 0f) px[y * n + x] = new Color(1, 1, 1, Mathf.Max(px[y * n + x].a, a));
                }
        }

        private static void SaveIconMail(string name)
        {
            int n = 36; var px = new Color[n * n];
            Seg(px, n, 5, 9, 31, 9, 1.3f); Seg(px, n, 5, 25, 31, 25, 1.3f);     // atas/bawah
            Seg(px, n, 5, 9, 5, 25, 1.3f); Seg(px, n, 31, 9, 31, 25, 1.3f);     // kiri/kanan
            Seg(px, n, 5, 25, 18, 17, 1.3f); Seg(px, n, 31, 25, 18, 17, 1.3f);  // flap "V"
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels(px); t.Apply(); Save(name, t, 0);
        }

        private static void SaveIconGear(string name)
        {
            int n = 36; var px = new Color[n * n]; float c = 18f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - c, dy = y + 0.5f - c;
                    float a = Mathf.Clamp01(1.5f - Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - 8f));
                    if (a > 0f) px[y * n + x] = new Color(1, 1, 1, a);
                }
            for (int k = 0; k < 8; k++)
            {
                float ang = k * 45f * Mathf.Deg2Rad;
                Seg(px, n, c + Mathf.Cos(ang) * 8, c + Mathf.Sin(ang) * 8, c + Mathf.Cos(ang) * 13, c + Mathf.Sin(ang) * 13, 1.5f);
            }
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels(px); t.Apply(); Save(name, t, 0);
        }

        private static void Save(string name, Texture2D tex, int slice = Slice)
        {
            string path = Dir + "/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spriteBorder = new Vector4(slice, slice, slice, slice);
            imp.spritePixelsPerUnit = 100;
            imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Bilinear;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.alphaIsTransparency = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }

        private static void ConfigurePanelSettings()
        {
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/QuantumUser/Resources/NexusPanelSettings.asset");
            if (ps == null) return;
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.match = 0.5f;
            EditorUtility.SetDirty(ps); AssetDatabase.SaveAssets();
        }

        [MenuItem("Nexus/Setup Sprite Test Scene")]
        public static void SetupTest()
        {
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/QuantumUser/Resources/NexusPanelSettings.asset");
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/NexusUI/SpriteTest.uxml");
            if (ps == null || uxml == null) { Debug.LogWarning("Jalankan Generate Lobby Sprites dulu, dan pastikan SpriteTest.uxml ada."); return; }

            if (Object.FindObjectOfType<Camera>() == null)
            {
                var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.04f, 0.08f, 0.16f);
            }
            var go = GameObject.Find("SpriteTest") ?? new GameObject("SpriteTest");
            var doc = go.GetComponent<UIDocument>() ?? go.AddComponent<UIDocument>();
            doc.panelSettings = ps; doc.visualTreeAsset = uxml;
            Selection.activeGameObject = go;
            Debug.Log("✓ SpriteTest siap — tekan Play untuk lihat semua sprite ukuran nyata + hover/klik.");
        }

        [MenuItem("Nexus/Setup New Lobby (Preview)")]
        public static void SetupLobby()
        {
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/QuantumUser/Resources/NexusPanelSettings.asset");
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/NexusUI/LobbyMain.uxml");
            if (ps == null || uxml == null) { Debug.LogWarning("Pastikan sprite sudah di-generate & LobbyMain.uxml ada."); return; }

            if (Object.FindObjectOfType<Camera>() == null)
            {
                var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.04f, 0.08f, 0.16f);
            }
            var go = GameObject.Find("NexusLobbyPreview") ?? new GameObject("NexusLobbyPreview");
            var doc = go.GetComponent<UIDocument>() ?? go.AddComponent<UIDocument>();
            doc.panelSettings = ps; doc.visualTreeAsset = uxml;
            // NexusGame dipasang juga → login UITK + tombol/akun BERFUNGSI (butuh backend nyala utk login).
            // Matchmaking tetap perlu scene online (RuntimeConfig + App Id) → di preview akan gagal (banner).
            var game = go.GetComponent<Nexus.NexusGame>() ?? go.AddComponent<Nexus.NexusGame>();
            game.UseNewLobby = true;
            var ctl = go.GetComponent<Nexus.NexusLobbyController>() ?? go.AddComponent<Nexus.NexusLobbyController>();
            ctl.document = doc; ctl.game = game;

            // Halaman Profil UITK (preview) — UIDocument sendiri di atas lobby.
            var profAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/NexusUI/ProfilePage.uxml");
            if (profAsset != null)
            {
                var pGo = GameObject.Find("NexusProfilePreview") ?? new GameObject("NexusProfilePreview");
                var pdoc = pGo.GetComponent<UIDocument>() ?? pGo.AddComponent<UIDocument>();
                pdoc.panelSettings = ps; pdoc.visualTreeAsset = profAsset; pdoc.sortingOrder = 1;
                var pctl = pGo.GetComponent<Nexus.ProfileController>() ?? pGo.AddComponent<Nexus.ProfileController>();
                pctl.document = pdoc; pctl.game = game;
                game.UseNewProfile = true;
            }
            Selection.activeGameObject = go;
            Debug.Log("✓ Lobby preview siap — tekan Play. Login UITK + tombol berfungsi (pastikan backend nyala). Match sungguhan: Nexus → Create Online Scene.");
        }

        private static Color C(int r, int g, int b, int a = 255) => new Color32((byte)r, (byte)g, (byte)b, (byte)a);
    }
}
