using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nexus
{
    /// <summary>
    /// Meta UI (UI Toolkit) — casual arcade lobby (inspirasi LostSaga, brand NEXUS SAGA).
    /// Home = lobby 3 kolom (sidebar tab · center battle+room+chat · mission/quest).
    /// Gradient (backdrop & tombol) di-generate di kode. Banyak placeholder (room/mission/chat) — disambung menyusul.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class NexusMetaUI : MonoBehaviour
    {
        public string BackendUrl = "http://localhost:5080";

        private VisualElement _root;
        private NexusBackend _backend;

        private enum Screen { Auth, Home }
        private Screen _screen = Screen.Auth;
        private enum BtnV { Blue, Green, Orange, Red, Gray }

        private bool _register, _busy, _showBoard;
        private string _err = "", _busyMsg = "", _toast = "", _userVal = "";
        private string _missionTab = "Mission", _missionSub = "Daily";
        private NexusBackend.LeaderRow[] _board;
        private TextField _fUser, _fPass, _fName;

        private static readonly string _deviceId = "nx-" + Guid.NewGuid().ToString("N");

        private void OnEnable()
        {
            foreach (var es in FindObjectsOfType<UnityEngine.EventSystems.EventSystem>())
                Destroy(es.gameObject); // legacy input → UITK DefaultEventSystem

            _root = GetComponent<UIDocument>().rootVisualElement;
            var theme = Resources.Load<StyleSheet>("NexusTheme");
            if (theme != null) _root.styleSheets.Add(theme);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font != null) _root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));

            _backend = new NexusBackend(BackendUrl);
            Render();
        }

        private void Render()
        {
            if (_root == null) return;
            _root.Clear();
            var screen = new VisualElement();
            screen.AddToClassList("nx-root");
            screen.style.flexGrow = 1;
            screen.style.backgroundImage = new StyleBackground(Backdrop());

            if (_screen == Screen.Home && !_showBoard) { screen.style.alignItems = Align.Stretch; BuildHome(screen); }
            else
            {
                screen.style.alignItems = Align.Center; screen.style.justifyContent = Justify.Center;
                screen.Add(Brand());
                if (_screen == Screen.Auth) BuildAuth(screen); else BuildBoard(screen);
            }
            _root.Add(screen);
        }

        private VisualElement Brand(float size = 42)
        {
            var wrap = new VisualElement(); wrap.AddToClassList("nx-center");
            var row = new VisualElement(); row.AddToClassList("nx-row"); row.style.justifyContent = Justify.Center;
            var n = new Label("NEXUS "); n.AddToClassList("nx-brand"); n.style.fontSize = size;
            var s = new Label("SAGA"); s.AddToClassList("nx-brand"); s.AddToClassList("nx-brand-accent"); s.style.fontSize = size;
            row.Add(n); row.Add(s);
            if (size >= 40) { var sub = new Label("NEO-VERIDIA ARENA"); sub.AddToClassList("nx-brand-sub"); wrap.Add(row); wrap.Add(sub); }
            else wrap.Add(row);
            return wrap;
        }

        // ================= AUTH =================
        private void BuildAuth(VisualElement root)
        {
            var card = Card();
            var head = new VisualElement(); head.AddToClassList("nx-card-head");
            var ht = new Label(_register ? "DAFTAR AKUN" : "MASUK"); ht.AddToClassList("nx-card-head-title"); head.Add(ht); card.Add(head);

            card.Add(Lbl("USERNAME", "nx-label"));
            _fUser = Field(false); _fUser.value = _userVal; _fUser.RegisterValueChangedCallback(e => _userVal = e.newValue); card.Add(_fUser);
            card.Add(Lbl("PASSWORD", "nx-label"));
            _fPass = Field(true); card.Add(_fPass);
            if (_register) { card.Add(Lbl("NAMA TAMPILAN", "nx-label")); _fName = Field(false); card.Add(_fName); }

            card.Add(Btn(_register ? "DAFTAR" : "MASUK", BtnV.Green, OnSubmit));
            card.Add(Btn("MAIN SEBAGAI GUEST", BtnV.Orange, OnGuest));
            card.Add(Link(_register ? "Sudah punya akun? Masuk" : "Belum punya akun? Daftar", () => { _register = !_register; _err = ""; Render(); }));
            if (_busy) card.Add(Lbl(_busyMsg, "nx-hint"));
            if (_err.Length > 0) card.Add(Lbl(_err, "nx-error"));
            root.Add(card);
        }

        private async void OnSubmit()
        {
            var u = (_fUser.value ?? "").Trim(); var p = _fPass.value ?? "";
            if (u.Length < 3 || p.Length < 6) { _err = "Username min 3 huruf, password min 6."; Render(); return; }
            _busy = true; _busyMsg = _register ? "Mendaftar..." : "Masuk..."; _err = ""; Render();
            bool ok = _register
                ? await _backend.RegisterAsync(u, p, string.IsNullOrWhiteSpace(_fName?.value) ? u : _fName.value.Trim())
                : await _backend.LoginAsync(u, p);
            _busy = false;
            if (ok) { await _backend.RefreshMeAsync(); _screen = Screen.Home; _err = ""; }
            else _err = _register ? "Daftar gagal (username dipakai / backend mati?)." : "Login gagal (salah / backend mati?).";
            Render();
        }

        private async void OnGuest()
        {
            _busy = true; _busyMsg = "Masuk sebagai guest..."; _err = ""; Render();
            bool ok = await _backend.GuestLoginAsync(_deviceId, "Guest" + UnityEngine.Random.Range(100, 999));
            _busy = false;
            if (ok) { await _backend.RefreshMeAsync(); _screen = Screen.Home; }
            else _err = "Guest gagal (backend nyala di " + BackendUrl + "?).";
            Render();
        }

        // ================= HOME (LOBBY 3 KOLOM) =================
        private void BuildHome(VisualElement root)
        {
            // Top strip: brand + gold
            var strip = new VisualElement(); strip.AddToClassList("nx-row");
            strip.style.paddingLeft = 18; strip.style.paddingRight = 18; strip.style.paddingTop = 10; strip.style.paddingBottom = 4;
            strip.Add(Brand(22));
            var sp0 = new VisualElement(); sp0.AddToClassList("nx-spacer"); strip.Add(sp0);
            strip.Add(Pill("GOLD", "0", true));
            root.Add(strip);

            var lobby = new VisualElement(); lobby.AddToClassList("nx-lobby");

            // --- SIDEBAR ---
            var side = new VisualElement(); side.AddToClassList("nx-sidebar");
            side.Add(Tab("B", new Color(0.33f, 0.6f, 0.9f), "BATTLE", "Lawan!", true, null, default, null));
            side.Add(Tab("F", new Color(0.9f, 0.4f, 0.35f), "FACTION", "War", false, "WAR", new Color(0.9f, 0.3f, 0.3f), () => Toast("Faction War — segera (Fase 3).")));
            side.Add(Tab("C", new Color(0.95f, 0.72f, 0.2f), "NEXUS CUP", "Turnamen", false, null, default, () => Toast("Turnamen — segera.")));
            side.Add(Tab("P", new Color(0.4f, 0.8f, 0.5f), "PLAZA", "Relax", false, null, default, () => Toast("Plaza sosial — segera.")));
            side.Add(Tab("M", new Color(0.7f, 0.55f, 0.95f), "MARKET", "Trade", false, "Rekrut", new Color(0.95f, 0.6f, 0.2f), () => Toast("Market — segera.")));
            var spSide = new VisualElement(); spSide.AddToClassList("nx-spacer"); side.Add(spSide);

            var rankP = new VisualElement(); rankP.AddToClassList("nx-panel-lt");
            var rk = new Label(_backend.AccountRank + "  Lv " + _backend.AccountLevel); rk.AddToClassList("nx-value"); rankP.Add(rk);
            var track = new VisualElement(); track.AddToClassList("nx-xp-track");
            var fill = new VisualElement(); fill.AddToClassList("nx-xp-fill");
            float pct = Mathf.Clamp01((_backend.AccountXp % 100) / 100f); if (pct < 0.05f) pct = 0.08f;
            fill.style.width = Length.Percent(pct * 100f);
            track.Add(fill); rankP.Add(track); side.Add(rankP);

            var hg = new VisualElement(); hg.AddToClassList("nx-row"); hg.style.marginTop = 8;
            var hq = Btn("HQ", BtnV.Blue, OpenProfileSoon); hq.style.flexGrow = 1; hq.style.marginRight = 6; hq.style.height = 42;
            var gd = Btn("GUILD", BtnV.Green, () => Toast("Guild — segera.")); gd.style.flexGrow = 1; gd.style.height = 42;
            hg.Add(hq); hg.Add(gd); side.Add(hg);
            lobby.Add(side);

            // --- CENTER ---
            var center = new VisualElement(); center.AddToClassList("nx-panel-lt"); center.style.flexGrow = 1; center.style.marginRight = 14;
            var actions = new VisualElement(); actions.AddToClassList("nx-row");
            actions.Add(ActionBtn("QUICK IN", BtnV.Orange, "Q", () => Toast("Quick Match — tersambung ke arena (langkah berikut).")));
            actions.Add(ActionBtn("BUAT ROOM", BtnV.Green, "+", () => Toast("Create Room — segera.")));
            actions.Add(ActionBtn("RANKED", BtnV.Blue, "R", () => Toast("Ranked — tersambung ke arena (langkah berikut).")));
            center.Add(actions);

            var grid = new VisualElement(); grid.AddToClassList("nx-roomgrid");
            for (int i = 0; i < 6; i++) grid.Add(RoomSlot());
            center.Add(grid);

            var pag = new VisualElement(); pag.AddToClassList("nx-row");
            pag.Add(SmallBtn("<")); var pg = new Label("  1  "); pg.AddToClassList("nx-value"); pag.Add(pg); pag.Add(SmallBtn(">"));
            var spP = new VisualElement(); spP.AddToClassList("nx-spacer"); pag.Add(spP);
            pag.Add(SmallBtn("REFRESH")); pag.Add(SmallBtn("LEADERBOARD")); center.Add(pag);

            var chat = new VisualElement(); chat.AddToClassList("nx-panel-lt"); chat.AddToClassList("nx-chatbox");
            var ci = new Label("[Info] Selamat datang di Nexus Saga, Runner!"); ci.AddToClassList("nx-chat-line"); ci.AddToClassList("nx-chat-info"); chat.Add(ci);
            var ci2 = new Label("[Info] Area chat lobby — segera aktif."); ci2.AddToClassList("nx-chat-line"); chat.Add(ci2);
            center.Add(chat);
            lobby.Add(center);

            // --- RIGHT (mission/quest) ---
            var right = new VisualElement(); right.AddToClassList("nx-panel-lt"); right.style.width = 262;
            var mtabs = new VisualElement(); mtabs.AddToClassList("nx-subtab");
            mtabs.Add(Chip("MISSION", _missionTab == "Mission", () => { _missionTab = "Mission"; Render(); }));
            mtabs.Add(Chip("QUEST", _missionTab == "Quest", () => { _missionTab = "Quest"; Render(); }));
            right.Add(mtabs);
            var msub = new VisualElement(); msub.AddToClassList("nx-subtab");
            foreach (var s in new[] { "Daily", "Weekly", "Monthly" }) { var ss = s; msub.Add(Chip(s.ToUpper(), _missionSub == s, () => { _missionSub = ss; Render(); })); }
            right.Add(msub);
            right.Add(MissionCard("M", new Color(0.33f, 0.6f, 0.9f), "Main 1 match", "Reward : 50 Gold", "0/1", true));
            right.Add(MissionCard("K", new Color(0.9f, 0.4f, 0.35f), "Dapatkan 5 kill", "Reward : 80 Gold", "0/5", false));
            right.Add(MissionCard("W", new Color(0.4f, 0.8f, 0.5f), "Menang 1 ranked", "Reward : 1 Ticket", "0/1", false));
            var mp = new Label("(mission placeholder — sistem quest menyusul)"); mp.AddToClassList("nx-hint"); right.Add(mp);
            var lo = Btn("LOGOUT", BtnV.Red, () => { _backend = new NexusBackend(BackendUrl); _screen = Screen.Auth; _err = ""; _toast = ""; Render(); });
            lo.style.height = 40; lo.style.marginTop = 10; right.Add(lo);
            lobby.Add(right);

            root.Add(lobby);

            if (_toast.Length > 0)
            {
                var t = new Label(_toast); t.AddToClassList("nx-hint");
                t.style.position = Position.Absolute; t.style.bottom = 6; t.style.left = 0; t.style.right = 0; t.style.unityTextAlign = TextAnchor.MiddleCenter;
                root.Add(t);
            }
        }

        private void OpenProfileSoon() => Toast("HQ / Profil penuh — langkah berikut (tombol Back & riwayat match).");

        // ================= LEADERBOARD =================
        private async void OpenBoard() { _board = null; _showBoard = true; Render(); _board = await _backend.GetLeaderboardAsync(15); Render(); }

        private void BuildBoard(VisualElement root)
        {
            var card = Card(); card.style.minWidth = 460;
            var head = new VisualElement(); head.AddToClassList("nx-card-head");
            var ht = new Label("LEADERBOARD"); ht.AddToClassList("nx-card-head-title"); head.Add(ht); card.Add(head);
            if (_board == null) card.Add(Lbl("Memuat...", "nx-hint"));
            else if (_board.Length == 0) card.Add(Lbl("(belum ada pemain)", "nx-hint"));
            else
                for (int i = 0; i < _board.Length; i++)
                {
                    var m = _board[i];
                    var row = new VisualElement(); row.AddToClassList("nx-row"); row.AddToClassList("nx-mt8");
                    var name = new Label($"{i + 1,2}.  {m.displayName}"); name.AddToClassList("nx-value");
                    var sp = new VisualElement(); sp.AddToClassList("nx-spacer");
                    var r = new Label($"{m.rating}   W{m.wins}/L{m.losses}"); r.AddToClassList("nx-label");
                    row.Add(name); row.Add(sp); row.Add(r); card.Add(row);
                }
            card.Add(Btn("KEMBALI", BtnV.Gray, () => { _showBoard = false; Render(); }));
            root.Add(card);
        }

        private void Toast(string m) { _toast = m; Render(); }

        // ================= HELPERS =================
        private static VisualElement Card() { var c = new VisualElement(); c.AddToClassList("nx-card"); return c; }
        private static Label Lbl(string t, string cls) { var l = new Label(t); l.AddToClassList(cls); return l; }
        private static TextField Field(bool pass) { var f = new TextField(); f.isPasswordField = pass; f.AddToClassList("nx-input"); return f; }

        private Button Btn(string t, BtnV v, Action onClick)
        {
            var b = new Button(onClick) { text = t };
            b.AddToClassList("nx-btn");
            b.style.backgroundImage = new StyleBackground(TexFor(v));
            return b;
        }
        private static Button Link(string t, Action onClick) { var b = new Button(onClick) { text = t }; b.AddToClassList("nx-btn"); b.AddToClassList("nx-btn--link"); return b; }

        private Button SmallBtn(string t) { var b = Btn(t, BtnV.Gray, () => { }); b.style.height = 32; b.style.fontSize = 11; b.style.paddingLeft = 10; b.style.paddingRight = 10; b.style.marginRight = 6; return b; }

        private VisualElement Pill(string k, string v, bool gold)
        {
            var p = new VisualElement(); p.AddToClassList("nx-pill");
            var kl = new Label(k + " "); kl.AddToClassList("nx-pill-k");
            var vl = new Label(v); vl.AddToClassList("nx-pill-v"); if (gold) vl.AddToClassList("nx-gold-v");
            p.Add(kl); p.Add(vl); return p;
        }

        private VisualElement Tab(string icL, Color icCol, string title, string sub, bool active, string badge, Color badgeCol, Action onClick)
        {
            var t = new VisualElement(); t.AddToClassList("nx-tab"); if (active) t.AddToClassList("nx-tab--active");
            var ic = new Label(icL); ic.AddToClassList("nx-tab-ic"); ic.style.backgroundColor = icCol; ic.style.unityTextAlign = TextAnchor.MiddleCenter; t.Add(ic);
            var col = new VisualElement();
            var tt = new Label(title); tt.AddToClassList("nx-tab-title"); col.Add(tt);
            var ts = new Label(sub); ts.AddToClassList("nx-tab-sub"); col.Add(ts);
            t.Add(col);
            if (!string.IsNullOrEmpty(badge)) { var sp = new VisualElement(); sp.AddToClassList("nx-spacer"); t.Add(sp); var bd = new Label(badge); bd.AddToClassList("nx-badge"); bd.style.backgroundColor = badgeCol; t.Add(bd); }
            if (onClick != null) t.AddManipulator(new Clickable(onClick));
            return t;
        }

        private VisualElement ActionBtn(string title, BtnV v, string icL, Action onClick)
        {
            var e = new VisualElement(); e.AddToClassList("nx-btn"); e.AddToClassList("nx-action");
            e.style.backgroundImage = new StyleBackground(TexFor(v));
            e.style.flexDirection = FlexDirection.Row; e.style.alignItems = Align.Center; e.style.justifyContent = Justify.Center;
            e.style.flexGrow = 1; e.style.marginRight = 8;
            var ic = new Label(icL); ic.AddToClassList("nx-ic"); ic.style.backgroundColor = new Color(1, 1, 1, 0.25f);
            ic.style.unityTextAlign = TextAnchor.MiddleCenter; ic.style.color = Color.white; ic.style.unityFontStyleAndWeight = FontStyle.Bold;
            var t = new Label(title); t.style.color = Color.white; t.style.unityFontStyleAndWeight = FontStyle.Bold; t.style.fontSize = 15;
            e.Add(ic); e.Add(t);
            e.AddManipulator(new Clickable(onClick));
            return e;
        }

        private VisualElement RoomSlot()
        {
            var r = new VisualElement(); r.AddToClassList("nx-room");
            var x = new Label("X"); x.AddToClassList("nx-room-x"); x.style.unityTextAlign = TextAnchor.MiddleCenter;
            var t = new Label("Buat Room Battle"); t.AddToClassList("nx-room-t");
            r.Add(x); r.Add(t);
            r.AddManipulator(new Clickable(() => Toast("Create Room — segera.")));
            return r;
        }

        private VisualElement Chip(string t, bool on, Action onClick)
        {
            var c = new Label(t); c.AddToClassList("nx-chip"); if (on) c.AddToClassList("nx-chip--on");
            c.AddManipulator(new Clickable(onClick));
            return c;
        }

        private VisualElement MissionCard(string icL, Color icCol, string title, string reward, string prog, bool hl)
        {
            var c = new VisualElement(); c.AddToClassList("nx-mcard"); if (hl) c.AddToClassList("nx-mcard--hl");
            var ic = new Label(icL); ic.AddToClassList("nx-mc-ic"); ic.style.backgroundColor = icCol; ic.style.unityTextAlign = TextAnchor.MiddleCenter; c.Add(ic);
            var col = new VisualElement(); col.style.flexGrow = 1;
            var tt = new Label(title); tt.AddToClassList("nx-mc-t"); col.Add(tt);
            var rr = new Label(reward); rr.AddToClassList("nx-mc-r"); col.Add(rr);
            c.Add(col);
            var pgl = new Label(prog); pgl.AddToClassList("nx-value"); pgl.style.fontSize = 12; c.Add(pgl);
            return c;
        }

        // ================= GRADIENT TEXTURES =================
        private static Texture2D _bg, _tBlue, _tGreen, _tOrange, _tRed, _tGray;

        private static Texture2D Backdrop()
        {
            if (_bg == null)
            {
                _bg = Resources.Load<Texture2D>("lobby_bg"); // opsional: drop gambar blur
                if (_bg == null) _bg = VGrad(new Color32(150, 200, 250, 255), new Color32(96, 150, 220, 255), new Color32(58, 104, 180, 255), 128);
            }
            return _bg;
        }

        private static Texture2D TexFor(BtnV v)
        {
            switch (v)
            {
                case BtnV.Green: return _tGreen ?? (_tGreen = Glossy(new Color32(96, 206, 104, 255)));
                case BtnV.Orange: return _tOrange ?? (_tOrange = Glossy(new Color32(250, 174, 60, 255)));
                case BtnV.Red: return _tRed ?? (_tRed = Glossy(new Color32(238, 96, 90, 255)));
                case BtnV.Gray: return _tGray ?? (_tGray = Glossy(new Color32(168, 178, 196, 255)));
                default: return _tBlue ?? (_tBlue = Glossy(new Color32(70, 146, 236, 255)));
            }
        }

        private static Texture2D Glossy(Color32 baseCol, int h = 48)
        {
            Color bc = baseCol;
            return VGrad(Color.Lerp(bc, Color.white, 0.55f), bc, Color.Lerp(bc, Color.black, 0.22f), h);
        }

        private static Texture2D VGrad(Color top, Color mid, Color bottom, int h)
        {
            var t = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < h; y++)
            {
                float f = (float)y / (h - 1);
                Color c = f >= 0.5f ? Color.Lerp(mid, top, (f - 0.5f) * 2f) : Color.Lerp(bottom, mid, f * 2f);
                t.SetPixel(0, y, c);
            }
            t.Apply();
            return t;
        }
    }
}
