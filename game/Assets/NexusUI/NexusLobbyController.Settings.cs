using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nexus
{
    /// <summary>
    /// Poin d (NX-13): Settings 5 tab (docs/31 §1.6), tombol "Segera Hadir", panel Pengumuman + hero terakhir (§1.7),
    /// daftar akun guest (K-10). Dipisah dari controller utama agar mudah dibaca.
    /// </summary>
    public sealed partial class NexusLobbyController
    {
        private VisualElement[] _setPages;
        private Label[] _setTabs;
        private Label _upMsg;
        private float _nextLobbyFetch;

        // Tombol yang belum punya sistem → nonaktif + tag. Mata uang "+" disembunyikan (belum ada ekonomi).
        private static readonly string[] SoonButtons =
            { "nav-shop", "nav-inventory", "nav-herodeck", "nav-quest", "nav-event", "nav-squad", "mail", "btn-hq", "btn-plaza", "btn-street" };
        private static readonly string[] HiddenButtons = { "gold-plus", "ticket-plus", "diamond-plus" };

        // Sama dengan NexusInput (gerak = axis default Unity: WASD / panah).
        private static readonly (string key, string act)[] Keys =
        {
            ("WASD", "Gerak 8 arah"), ("J", "Pukul / tembak"), ("K", "Skill hero"), ("Space", "Lompat"),
            ("Shift", "Dash"), ("L", "Guard"), ("H", "Ganti hero"), ("Esc", "Tutup / batal"),
        };

        private void BindSoon()
        {
            foreach (var n in SoonButtons)
            {
                var b = _root.Q<Button>(n);
                if (b == null) continue;
                b.SetEnabled(false);
                b.AddToClassList("nxl-soon");
                var tag = new Label("Segera") { pickingMode = PickingMode.Ignore };
                tag.AddToClassList("nxl-soon-tag"); tag.AddToClassList("nxl-soon-tag--badge"); // poin 3: badge pojok, bukan teks menimpa ikon
                b.Add(tag);
            }
            foreach (var n in HiddenButtons) ShowEl(_root.Q(n), false);
        }

        private void BindSettings()
        {
            NexusSettings.ApplyAll();

            _setPages = Enumerable.Range(0, 5).Select(i => _root.Q<VisualElement>("set-page-" + i)).ToArray();
            _setTabs = Enumerable.Range(0, 5).Select(i => _root.Q<Label>("set-tab-" + i)).ToArray();
            for (int i = 0; i < _setTabs.Length; i++)
            {
                int idx = i;
                if (_setTabs[i] != null) _setTabs[i].RegisterCallback<ClickEvent>(_ => ShowSetTab(idx));
            }

            // --- Grafis ---
            var res = _root.Q<DropdownField>("set-res");
            if (res != null)
            {
                var list = NexusSettings.Resolutions();
                if (!list.Contains(NexusSettings.Resolution)) list.Add(NexusSettings.Resolution);
                res.choices = list; res.SetValueWithoutNotify(NexusSettings.Resolution);
                res.RegisterValueChangedCallback(e => NexusSettings.Resolution = e.newValue);
            }
            var mode = _root.Q<DropdownField>("set-mode");
            if (mode != null)
            {
                mode.choices = NexusSettings.ScreenModes.ToList(); mode.index = NexusSettings.ScreenMode;
                mode.RegisterValueChangedCallback(_ => NexusSettings.ScreenMode = mode.index);
            }
            BindToggle("set-vsync", NexusSettings.VSync, v => NexusSettings.VSync = v);
            var fps = _root.Q<DropdownField>("set-fps");
            if (fps != null)
            {
                fps.choices = NexusSettings.FpsCaps.Select(c => c > 0 ? c + " FPS" : "Tanpa batas").ToList();
                fps.index = Mathf.Max(0, System.Array.IndexOf(NexusSettings.FpsCaps, NexusSettings.FpsCap));
                fps.RegisterValueChangedCallback(_ => NexusSettings.FpsCap = NexusSettings.FpsCaps[fps.index]);
            }
            var q = _root.Q<DropdownField>("set-quality");
            if (q != null)
            {
                q.choices = QualitySettings.names.ToList(); q.index = NexusSettings.Quality;
                q.RegisterValueChangedCallback(_ => NexusSettings.Quality = q.index);
            }

            // --- Audio ---
            var master = _root.Q<SliderInt>("set-master");
            if (master != null)
            {
                master.SetValueWithoutNotify(NexusSettings.MasterVolume);
                master.RegisterValueChangedCallback(e => NexusSettings.MasterVolume = e.newValue);
            }
            BindToggle("set-mute", NexusSettings.Mute, v => NexusSettings.Mute = v);

            // --- Kontrol (daftar baca-saja; rebinding Segera Hadir) ---
            var keys = _root.Q<VisualElement>("set-keys");
            if (keys != null)
                foreach (var (key, act) in Keys)
                {
                    var row = new VisualElement(); row.AddToClassList("nxl-set-key");
                    var cap = new Label(key); cap.AddToClassList("nxl-set-keycap"); row.Add(cap);
                    var a = new Label(act); a.AddToClassList("nxl-set-val"); row.Add(a);
                    keys.Add(row);
                }

            // --- Antarmuka ---
            BindToggle("set-showfps", NexusSettings.ShowFps, v => NexusSettings.ShowFps = v);

            // --- Akun ---
            Click("set-acc-copy", () => { var b = game != null ? game.Backend : null; if (b != null) GUIUtility.systemCopyBuffer = b.PlayerId; });
            var pass = _root.Q<TextField>("set-up-pass"); if (pass != null) pass.isPasswordField = true;
            _upMsg = _root.Q<Label>("set-up-msg");
            Click("set-up-btn", UpgradeAccount);

            ShowSetTab(0);
        }

        private void BindToggle(string name, bool value, System.Action<bool> set)
        {
            var t = _root.Q<Toggle>(name);
            if (t == null) return;
            t.SetValueWithoutNotify(value);
            t.RegisterValueChangedCallback(e => set(e.newValue));
        }

        private void OpenSettings()
        {
            RefreshAccountTab();
            ShowEl(_settingsOverlay, true);
        }

        private void ShowSetTab(int idx)
        {
            for (int i = 0; i < _setPages.Length; i++)
            {
                ShowEl(_setPages[i], i == idx);
                if (_setTabs[i] != null) _setTabs[i].EnableInClassList("nxl-tab--active", i == idx);
            }
            if (idx == 4) RefreshAccountTab();
        }

        private void RefreshAccountTab()
        {
            var b = game != null ? game.Backend : null;
            bool guest = b == null || string.IsNullOrEmpty(b.Username);
            SetText("set-acc-name", b != null && !string.IsNullOrEmpty(b.DisplayName) ? b.DisplayName : "-");
            SetText("set-acc-user", b == null ? "-" : guest ? "Guest (belum terdaftar)" : b.Username);
            SetText("set-acc-id", b != null && !string.IsNullOrEmpty(b.PlayerId) ? b.PlayerId : "-");
            ShowEl(_root.Q("set-upgrade"), b != null && b.LoggedIn && guest);
        }

        private async void UpgradeAccount()
        {
            var b = game != null ? game.Backend : null;
            if (b == null || _upMsg == null) return;
            string user = _root.Q<TextField>("set-up-user")?.value?.Trim() ?? "";
            string pass = _root.Q<TextField>("set-up-pass")?.value ?? "";
            if (user.Length < 3 || user.Length > 24) { _upMsg.text = "Username 3–24 karakter."; return; }
            if (pass.Length < 6) { _upMsg.text = "Password minimal 6 karakter."; return; }
            var btn = _root.Q<Button>("set-up-btn"); btn?.SetEnabled(false);
            _upMsg.text = "Mendaftarkan...";
            string err = await b.UpgradeAccountAsync(user, pass);
            btn?.SetEnabled(true);
            _upMsg.text = err ?? "Akun terdaftar. Berikutnya login pakai username ini.";
            if (err == null) { RefreshAccountTab(); RefreshAccount(); }
        }

        // ---- Lobby 1.7: pengumuman + hero terakhir. Dipanggil saat lobby tampil & tiap 60 dtk. ----
        private async void FetchLobbyData()
        {
            var b = game != null ? game.Backend : null;
            if (b == null || !b.LoggedIn) return;

            var anns = await b.GetAnnouncementsAsync();
            var list = _root.Q<VisualElement>("ann-list");
            if (list != null)
            {
                list.Clear();
                if (anns == null) list.Add(AnnNote("Gagal memuat pengumuman. Dicoba lagi otomatis."));   // Q45: gagal ≠ kosong
                else if (anns.Length == 0) list.Add(AnnNote("Belum ada pengumuman."));
                else foreach (var a in anns)
                {
                    var item = new VisualElement(); item.AddToClassList("nxl-ann-item");
                    var k = new Label(a.kind == "event" ? "EVENT" : "INFO"); k.AddToClassList("nxl-ann-kind"); item.Add(k);
                    var t = new Label(a.title); t.AddToClassList("nxl-ann-title"); item.Add(t);
                    if (!string.IsNullOrEmpty(a.body)) { var bd = new Label(a.body); bd.AddToClassList("nxl-ann-body"); item.Add(bd); }
                    list.Add(item);
                }
            }

            var rows = await b.GetMatchHistoryAsync();
            int hero = rows != null && rows.Length > 0 ? rows[0].heroId : -1;
            var heroEl = _root.Q<VisualElement>("lobby-hero");
            if (heroEl != null)
            {
                heroEl.EnableInClassList("nxl-hero-titan", hero != 1);
                heroEl.EnableInClassList("nxl-hero-pulse", hero == 1);
            }
            SetText("lobby-hero-name", hero == 1 ? "PULSE-X" : "TITAN-01");
            SetText("lobby-hero-sub", hero < 0 ? "Hero default" : "Terakhir dimainkan");
        }

        private static Label AnnNote(string text) { var l = new Label(text); l.AddToClassList("nxl-ann-empty"); return l; }
    }
}
