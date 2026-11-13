using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nexus
{
    /// <summary>
    /// Tahap 3 — controller lobby UI Toolkit (LobbyMain.uxml). Berdampingan dengan IMGUI NexusGame
    /// (toggle NexusGame.UseNewLobby). Binding by element name; semua aksi memanggil handler matchmaking
    /// NexusGame yang sudah ada (QuickMatch/CreateRoom/Ranked/Logout). TANPA data dummy: currency 0,
    /// nama/level/rank dari NexusBackend, chat echo lokal.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed partial class NexusLobbyController : MonoBehaviour
    {
        [Tooltip("NexusGame (matchmaking + akun). Auto-cari bila kosong.")]
        public NexusGame game;
        [Tooltip("UIDocument dengan LobbyMain.uxml. Auto-ambil dari GameObject ini bila kosong.")]
        public UIDocument document;

        private VisualElement _root;
        private Label _name, _level, _rankTier, _rankSub, _modeTitle, _modeDesc, _searchStatus, _searchTimer, _discMsg;
        private VisualElement _createRoom, _rankMini, _searchOverlay, _discBanner, _settingsOverlay;
        private VisualElement[] _dots;
        private Button _primary;
        private ScrollView _chatLog;
        private TextField _chatInput;

        // Layar: lobby vs auth
        private VisualElement _lobbyBody, _authScreen;
        private int _screenState = -1; // 0 auth, 1 lobby, 2 lain (IMGUI)

        // Auth (login/daftar UITK)
        private Label _authMsg, _authSubtitle, _authNameLabel, _authToggle;
        private TextField _authUser, _authPass, _authName;
        private Button _authSubmit, _authEye;
        private bool _registerMode, _showPass;

        // Faksi (onboarding UITK — docs/35)
        private VisualElement _factionScreen, _facOrde, _facLegion;
        private Label _facOrdeNeed, _facLegionNeed, _facMsg;
        private Button _facConfirm;
        private int _facSelected;          // 0 belum pilih, 1 ORDE, 2 LEGION
        private int _facNeeds = -1;        // -1 belum di-fetch; 0 seimbang; 1/2 butuh pejuang
        private int _facBonus;
        private bool _facFetching;

        private GameObject _quantumStats; // overlay debug Quantum — dev-only, di match saja

        // Ruang Tim (Party Lobby)
        private VisualElement _partyScreen, _partySlots, _partyChat, _partyInvite, _opponentCard;
        private VisualElement _ladderLayout, _teamLayout, _vsOverlay, _vsSlots, _partyHeader;
        private VisualElement _graphCanvas, _graphArea, _graphEmpty, _graphDates;
        private float[] _graphR;
        private Label _partyTitle, _partySub;
        private Button _partyStart, _partyCancel, _partyCreateRoom, _partyBack, _ladderStart, _ladderCancel;
        private ScrollView _ldChatLog;
        private TextField _ldChatInput;
        private bool _inParty, _partySolo, _vsBuilt;
        private int _partyMode;

        private int _mode;
        private bool _bound, _wasConnecting;
        private float _searchStart, _nextAccountRefresh;

        private struct ModeDef { public string Title, Desc, Primary; public bool Room, Ranked, Disabled; public Action Act; }
        private ModeDef[] _modes;

        private void Awake()
        {
            if (document == null) document = GetComponent<UIDocument>();
            if (game == null) game = FindObjectOfType<NexusGame>();
            if (game == null) // fallback: lobby butuh NexusGame (login + matchmaking). Buat bila belum ada.
            {
                game = gameObject.AddComponent<NexusGame>();
                game.UseNewLobby = true;
            }
        }

        private void OnEnable() { _bound = false; TryBind(); }
        private void OnDisable() { _bound = false; }

        private void TryBind()
        {
            if (_bound || document == null) return;
            _root = document.rootVisualElement;
            if (_root == null) return; // UIDocument belum bangun root → coba lagi di Update

            _modes = new[]
            {
                new ModeDef { Title = "RANKED",      Desc = "Solo 1v1 · Kompetitif",      Primary = "MAIN",         Ranked = true,   Act = () => game.UiRanked() },
                new ModeDef { Title = "BATTLE",      Desc = "Casual · tanpa peringkat",   Primary = "QUICK JOIN",   Room = true,     Act = () => game.UiQuickMatch() },
                new ModeDef { Title = "FACTION WAR · NEXUS CUP", Desc = "Mode 5v5 — Segera Hadir", Primary = "SEGERA HADIR", Disabled = true, Act = () => { } },
            };

            _name = _root.Q<Label>("player-name");
            _level = _root.Q<Label>("player-level");
            _rankTier = _root.Q<Label>("rank-tier");
            _rankSub = _root.Q<Label>("rank-sub");
            _modeTitle = _root.Q<Label>("mode-title");
            _modeDesc = _root.Q<Label>("mode-desc");
            _rankMini = _root.Q<VisualElement>("rank-mini");
            _dots = new[] { _root.Q<VisualElement>("dot-0"), _root.Q<VisualElement>("dot-1"), _root.Q<VisualElement>("dot-2"), _root.Q<VisualElement>("dot-3") };
            _primary = _root.Q<Button>("btn-primary");
            _createRoom = _root.Q<Button>("btn-create-room");
            _partyScreen = _root.Q<VisualElement>("party-screen");
            _partySlots = _root.Q<VisualElement>("party-slots");
            _partyTitle = _root.Q<Label>("party-title");
            _partySub = _root.Q<Label>("party-sub");
            _partyStart = _root.Q<Button>("party-start");
            _partyCancel = _root.Q<Button>("party-cancel");
            _partyCreateRoom = _root.Q<Button>("party-create-room");
            _partyChat = _root.Q<VisualElement>("party-chat");
            _partyInvite = _root.Q<VisualElement>("party-invite");
            _partyBack = _root.Q<Button>("party-back");
            _ladderLayout = _root.Q<VisualElement>("ladder-layout");
            _teamLayout = _root.Q<VisualElement>("team-layout");
            _vsOverlay = _root.Q<VisualElement>("vs-overlay");
            _vsSlots = _root.Q<VisualElement>("vs-slots");
            _ladderStart = _root.Q<Button>("ladder-start");
            _ladderCancel = _root.Q<Button>("ladder-cancel");
            _ldChatLog = _root.Q<ScrollView>("ld-chat-log");
            _ldChatInput = _root.Q<TextField>("ld-chat-input");
            _partyHeader = _root.Q<VisualElement>("party-header");
            _graphCanvas = _root.Q<VisualElement>("ld-graph-canvas");
            _graphArea = _root.Q<VisualElement>("ld-graph-area");
            _graphEmpty = _root.Q<VisualElement>("ld-graph-empty");
            _graphDates = _root.Q<VisualElement>("ld-graph-dates");
            if (_graphCanvas != null) _graphCanvas.generateVisualContent += OnDrawGraph;
            _searchStatus = _root.Q<Label>("search-status");
            _searchTimer = _root.Q<Label>("search-timer");
            _discMsg = _root.Q<Label>("disc-msg");
            _searchOverlay = _root.Q<VisualElement>("searching-overlay");
            _discBanner = _root.Q<VisualElement>("disconnect-banner");
            _settingsOverlay = _root.Q<VisualElement>("settings-overlay");
            _chatLog = _root.Q<ScrollView>("chat-log");
            _chatInput = _root.Q<TextField>("chat-input");

            Click("mode-prev", () => CycleMode(-1));
            Click("mode-next", () => CycleMode(1));
            // HQ / Plaza / Street Room / bottom nav / Mail: nonaktif + "SEGERA HADIR" (BindSoon).
            Click("btn-create-room", () => { if (game != null) game.UiCreateRoom(); });
            Click("party-back", LeaveParty);
            Click("party-start", StartParty);
            Click("party-cancel", () => { if (game != null) game.UiCancel(); });
            Click("party-create-room", () => { if (game != null) game.UiCreateRoom(); });
            Click("ladder-start", StartParty);
            Click("ladder-cancel", () => { if (game != null) game.UiCancel(); });
            Click("ld-exit", LeaveParty);
            Click("ld-chat-send", SendLadderChat);
            if (_ldChatInput != null) _ldChatInput.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) SendLadderChat(); });
            ClickEl("ltab-matches", () => ShowLadderTab(0));
            ClickEl("ltab-top", () => ShowLadderTab(1));
            ClickEl("ltab-graph", () => ShowLadderTab(2));
            Click("settings", OpenSettings);
            Click("btn-logout", () => { ShowEl(_settingsOverlay, false); if (game != null) game.UiLogout(); });
            Click("btn-settings-close", () => ShowEl(_settingsOverlay, false));
            Click("btn-search-cancel", () => { if (game != null) game.UiCancel(); });
            Click("btn-reconnect", () => { if (game != null) game.ClearError(); RunMode(); });
            Click("btn-disc-close", () => { if (game != null) game.ClearError(); });
            Click("chat-send", SendChat);

            if (_primary != null) _primary.clicked += RunMode;
            var profile = _root.Q<VisualElement>("profile-card");
            if (profile != null) profile.RegisterCallback<ClickEvent>(_ => { if (game != null) game.UiOpenProfile(); });
            if (_chatInput != null) _chatInput.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) SendChat(); });

            // Layar
            _lobbyBody = _root.Q<VisualElement>("lobby-body");
            _authScreen = _root.Q<VisualElement>("auth-screen");
            _quantumStats = GameObject.Find("QuantumStats");

            // Auth (login/daftar)
            _authMsg = _root.Q<Label>("auth-msg");
            _authSubtitle = _root.Q<Label>("auth-subtitle");
            _authUser = _root.Q<TextField>("auth-user");
            _authPass = _root.Q<TextField>("auth-pass");
            _authName = _root.Q<TextField>("auth-name");
            _authNameLabel = _root.Q<Label>("auth-name-label");
            _authSubmit = _root.Q<Button>("auth-submit");
            _authToggle = _root.Q<Label>("auth-toggle");
            _authEye = _root.Q<Button>("auth-eye");
            Click("auth-submit", SubmitAuth);
            Click("auth-guest", () => { if (game != null) game.UiGuest(); });
            if (_authToggle != null) _authToggle.RegisterCallback<ClickEvent>(_ => ToggleAuthMode());
            if (_authEye != null) _authEye.clicked += ToggleShowPass;
            if (_authPass != null) { _authPass.isPasswordField = true; _authPass.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) SubmitAuth(); }); }
            if (_authUser != null) _authUser.RegisterCallback<KeyDownEvent>(e => { if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && _authPass != null) _authPass.Focus(); });
            ApplyAuthMode();

            // Faksi onboarding
            _factionScreen = _root.Q<VisualElement>("faction-screen");
            _facOrde = _root.Q<VisualElement>("fac-orde");
            _facLegion = _root.Q<VisualElement>("fac-legion");
            _facOrdeNeed = _root.Q<Label>("fac-orde-need");
            _facLegionNeed = _root.Q<Label>("fac-legion-need");
            _facMsg = _root.Q<Label>("fac-msg");
            _facConfirm = _root.Q<Button>("fac-confirm");
            if (_facOrde != null) _facOrde.RegisterCallback<ClickEvent>(_ => SelectFaction(1));
            if (_facLegion != null) _facLegion.RegisterCallback<ClickEvent>(_ => SelectFaction(2));
            Click("fac-confirm", () => { if (game != null && _facSelected != 0) game.UiSetFaction(_facSelected); });

            ShowEl(_searchOverlay, false); ShowEl(_discBanner, false); ShowEl(_settingsOverlay, false);
            BindSoon(); BindSettings();
            ApplyMode(); RefreshAccount();
            _bound = true;
        }

        private void Update()
        {
            if (!_bound) { TryBind(); if (!_bound) return; }
            if (game == null) return;

            // Overlay debug Quantum: hanya di match + hanya editor/dev build (jangan timpa lobby).
            if (_quantumStats != null)
            {
                bool devStats = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                devStats = game.IsInMatch;
#endif
                if (_quantumStats.activeSelf != devStats) _quantumStats.SetActive(devStats);
            }

            // 0 = layar login, 1 = lobby, 2 = Profile/InMatch (IMGUI lama)
            int st = game.IsAuth ? 0 : ((game.IsMenu || game.IsConnecting) ? 1 : 2);
            if (st != _screenState) { ApplyScreen(st); _screenState = st; }
            if (st == 2) return;

            if (st == 0) // layar login
            {
                if (_authMsg != null) _authMsg.text = game.AuthMsg;
                if (_authSubmit != null)
                {
                    _authSubmit.SetEnabled(!game.AuthBusy);
                    _authSubmit.text = game.AuthBusy ? "MASUK..." : (_registerMode ? "DAFTAR" : "LOGIN");
                }
                return;
            }

            // Onboarding faksi: login sukses tapi belum punya faksi → pilih dulu sebelum masuk lobby (docs/35).
            if (game.IsMenu && game.NeedsFaction)
            {
                ShowEl(_factionScreen, true);
                ShowEl(_lobbyBody, false);
                ShowEl(_partyScreen, false);
                UpdateFactionScreen();
                return;
            }
            ShowEl(_factionScreen, false);

            if (_settingsOverlay != null && _settingsOverlay.resolvedStyle.display == DisplayStyle.Flex && Input.GetKeyDown(KeyCode.Escape))
            { ShowEl(_settingsOverlay, false); return; }

            // Ruang Tim: aktif saat party terbuka — di menu ATAU sedang mencari lawan.
            bool partyActive = _inParty && (game.IsMenu || game.IsConnecting);
            ShowEl(_partyScreen, partyActive);
            ShowEl(_lobbyBody, !partyActive);
            if (partyActive)
            {
                UpdatePartyState();
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    if (game.IsConnecting) game.UiCancel(); // Esc 1x saat mencari: batalkan pencarian
                    else LeaveParty();                       // Esc lagi: keluar ke lobby
                }
                else if (game.IsMenu && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))) StartParty();
            }

            if (game.IsMenu && !_inParty) // panah ganti mode hanya di lobby (bukan di Ruang Tim)
            {
                if (Input.GetKeyDown(KeyCode.LeftArrow)) CycleMode(-1);
                else if (Input.GetKeyDown(KeyCode.RightArrow)) CycleMode(1);
            }

            bool conn = game.IsConnecting;
            if (conn && !_wasConnecting) _searchStart = Time.unscaledTime;
            _wasConnecting = conn;
            ShowEl(_searchOverlay, conn);
            if (conn)
            {
                if (_searchStatus != null) _searchStatus.text = string.IsNullOrEmpty(game.StatusText) ? "Mencari lawan..." : game.StatusText;
                if (_searchTimer != null) _searchTimer.text = Mathf.FloorToInt(Time.unscaledTime - _searchStart) + "s";
            }

            bool err = !string.IsNullOrEmpty(game.LastError);
            ShowEl(_discBanner, err);
            if (err && _discMsg != null) _discMsg.text = game.LastError;

            if (game.IsMenu && Time.unscaledTime >= _nextAccountRefresh) { RefreshAccount(); _nextAccountRefresh = Time.unscaledTime + 1f; }
            if (game.IsMenu && !partyActive && Time.unscaledTime >= _nextLobbyFetch) { FetchLobbyData(); _nextLobbyFetch = Time.unscaledTime + 60f; }
        }

        private void ApplyScreen(int st)
        {
            if (st != 1) _inParty = false; // keluar lobby (auth/match/profile) → reset Ruang Tim
            _root.style.display = (st == 2) ? DisplayStyle.None : DisplayStyle.Flex;
            ShowEl(_authScreen, st == 0);
            if (st == 1 && _lobbyBody != null) // fade-in lobby ~0.3s
            {
                _lobbyBody.style.opacity = 0f;
                _lobbyBody.style.display = DisplayStyle.Flex;
                _lobbyBody.schedule.Execute(() => { if (_lobbyBody != null) _lobbyBody.style.opacity = 1f; }).StartingIn(24);
            }
            else if (st != 1) ShowEl(_lobbyBody, false);
            if (st != 1) { ShowEl(_factionScreen, false); _facSelected = 0; _facNeeds = -1; } // reset onboarding di luar lobby
            if (st == 0) _wasConnecting = false;
            if (st == 1) _nextLobbyFetch = 0f; // kembali ke lobby (mis. habis match) → muat ulang pengumuman & hero terakhir
        }

        // --- Onboarding faksi (docs/35) ---
        private void SelectFaction(int f)
        {
            _facSelected = f;
            if (_facOrde != null) _facOrde.EnableInClassList("nxl-faccard--sel", f == 1);
            if (_facLegion != null) _facLegion.EnableInClassList("nxl-faccard--sel", f == 2);
        }

        private void UpdateFactionScreen()
        {
            if (_facNeeds < 0 && !_facFetching) FetchFactionStatus();     // sekali: "Butuh Pejuang"
            if (_facConfirm != null)
            {
                bool busy = game != null && game.FactionBusy;
                _facConfirm.SetEnabled(_facSelected != 0 && !busy);
                _facConfirm.text = busy ? "MENGUNCI..." : "KUNCI FAKSI";
            }
            if (_facMsg != null && game != null) _facMsg.text = game.FactionMsg;
        }

        private async void FetchFactionStatus()
        {
            if (game == null || game.Backend == null) return;
            _facFetching = true;
            var s = await game.Backend.GetFactionsAsync();
            _facFetching = false;
            if (s == null) { _facNeeds = 0; return; }
            _facNeeds = s.needsWarriors; _facBonus = s.bonusPercent;
            string tag = _facBonus > 0 ? $"BUTUH PEJUANG +{_facBonus}%" : "BUTUH PEJUANG";
            if (_facOrdeNeed != null) { _facOrdeNeed.text = tag; ShowEl(_facOrdeNeed, _facNeeds == 1); }
            if (_facLegionNeed != null) { _facLegionNeed.text = tag; ShowEl(_facLegionNeed, _facNeeds == 2); }
        }

        private void ToggleShowPass()
        {
            _showPass = !_showPass;
            if (_authPass != null) _authPass.isPasswordField = !_showPass;
            if (_authEye != null)
            {
                _authEye.EnableInClassList("nxl-ic-eye", !_showPass);
                _authEye.EnableInClassList("nxl-ic-eye-off", _showPass);
            }
        }

        private void ToggleAuthMode() { _registerMode = !_registerMode; ApplyAuthMode(); }

        private void ApplyAuthMode()
        {
            if (_authSubmit != null && (game == null || !game.AuthBusy)) _authSubmit.text = _registerMode ? "DAFTAR" : "LOGIN";
            if (_authToggle != null) _authToggle.text = _registerMode ? "Sudah punya akun? Login" : "Belum punya akun? Daftar";
            if (_authSubtitle != null) _authSubtitle.text = _registerMode ? "Buat akun baru" : "Masuk untuk bermain";
            ShowEl(_authName, _registerMode);
            ShowEl(_authNameLabel, _registerMode);
        }

        private void SubmitAuth()
        {
            if (game == null) return;
            string u = _authUser != null && _authUser.value != null ? _authUser.value.Trim() : "";
            string p = _authPass != null && _authPass.value != null ? _authPass.value : "";
            string nm = _authName != null && _authName.value != null ? _authName.value.Trim() : "";
            if (_registerMode) game.UiRegister(u, p, string.IsNullOrEmpty(nm) ? u : nm);
            else game.UiLogin(u, p);
        }

        private void RunMode() { OpenParty(); } // MAIN → buka Ruang Tim
        private void CycleMode(int d) { _mode = (_mode + d + _modes.Length) % _modes.Length; ApplyMode(); }

        // ---- Ruang Tim (Party Lobby) ----
        private void OpenParty() { if (_modes == null) return; _partyMode = _mode; _inParty = true; ApplyParty(); }
        private void LeaveParty() { _inParty = false; }
        private void StartParty()
        {
            if (_modes == null) return; var m = _modes[_partyMode]; if (m.Disabled) return;
            _searchStart = Time.unscaledTime; m.Act(); // MULAI → matchmaking (handler mode)
        }

        private void ApplyParty()
        {
            int n = NexusPartyConfig.ForMode(_partyMode);
            var m = _modes[_partyMode];
            _partySolo = n == 1;
            _vsBuilt = false;
            if (_partyTitle != null) _partyTitle.text = m.Title;
            if (_partySub != null) _partySub.text = _partySolo ? "Solo 1v1 · Ladder" : ("Ruang Tim · " + n + " Pemain");
            ShowEl(_ladderLayout, _partySolo);
            ShowEl(_teamLayout, !_partySolo);
            ShowEl(_partyHeader, !_partySolo); // header luar hanya mode tim; ladder punya header jendela sendiri
            ShowEl(_vsOverlay, false);
            ShowEl(_partyCancel, false); ShowEl(_ladderCancel, false);
            if (_partySolo)
            {
                FillLadder(); FetchLadderData(); ShowLadderTab(2); // Grafik = tab default
            }
            else
            {
                ShowEl(_partyChat, true); ShowEl(_partyInvite, true);
                ShowEl(_partyCreateRoom, m.Room && !m.Disabled);
                BuildStage(n, m.Ranked);
            }
        }

        // Status MULAI / mencari lawan / Batal / Back — dipanggil tiap frame saat party aktif.
        private void UpdatePartyState()
        {
            var m = _modes[_partyMode];
            bool searching = game.IsConnecting;
            bool found = searching && game.OpponentFound;
            var start = _partySolo ? _ladderStart : _partyStart;
            var cancel = _partySolo ? _ladderCancel : _partyCancel;
            if (start != null)
            {
                if (searching)
                {
                    int t = Mathf.Max(0, Mathf.FloorToInt(Time.unscaledTime - _searchStart));
                    start.text = found ? "LAWAN DITEMUKAN" : ("MENCARI LAWAN  " + (t / 60).ToString("00") + ":" + (t % 60).ToString("00"));
                    start.SetEnabled(false);
                }
                else { start.SetEnabled(!m.Disabled); start.text = m.Disabled ? "SEGERA HADIR" : "MULAI"; }
                start.EnableInClassList("nxl-btn--orange", searching || !m.Disabled);
                start.EnableInClassList("nxl-btn--gray", m.Disabled && !searching);
            }
            ShowEl(cancel, searching && !found);
            if (_partyBack != null) _partyBack.SetEnabled(!searching);
            if (_opponentCard != null)
                _opponentCard.style.opacity = (searching && !found) ? (0.45f + 0.55f * Mathf.PingPong(Time.unscaledTime * 1.6f, 1f)) : 1f;

            // VS overlay saat lawan ditemukan (~1.8s)
            ShowEl(_vsOverlay, found);
            if (found && !_vsBuilt) { BuildVs(); _vsBuilt = true; }
            else if (!found) _vsBuilt = false;
        }

        private void BuildStage(int n, bool ranked)
        {
            if (_partySlots == null) return;
            _partySlots.Clear();
            _opponentCard = null;
            if (n == 1)
            {
                _partySlots.Add(PlayerBanner(ranked));
                var vs = new Label("VS"); vs.AddToClassList("nxl-vs");
                _partySlots.Add(vs);
                _partySlots.Add(OpponentBanner());
            }
            else
            {
                for (int i = 0; i < n; i++) _partySlots.Add(i == 0 ? PlayerBanner(ranked) : EmptyBanner());
            }
        }

        private VisualElement BannerWrap(VisualElement card)
        {
            var wrap = new VisualElement(); wrap.AddToClassList("nxl-pbanner-wrap");
            var plat = new VisualElement(); plat.AddToClassList("nxl-platform");
            wrap.Add(card); wrap.Add(plat);
            return wrap;
        }

        private VisualElement PlayerBanner(bool ranked)
        {
            var b = game != null ? game.Backend : null;
            bool on = b != null && b.LoggedIn;
            string name = PlayerName(b);
            int level = on ? b.AccountLevel : 1;
            string tier = on && !string.IsNullOrEmpty(b.RankTier) ? b.RankTier : "UNRANKED";

            var card = new VisualElement(); card.AddToClassList("nxl-pbanner");
            var crown = new Label("KETUA"); crown.AddToClassList("nxl-pb-crown");
            var av = new Label(name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "N");
            av.AddToClassList("nxl-ic"); av.AddToClassList("nxl-pb-avatar");
            var nm = new Label(name); nm.AddToClassList("nxl-pb-name");
            var lv = new Label("Level " + level); lv.AddToClassList("nxl-pb-sub");
            card.Add(crown); card.Add(av); card.Add(nm); card.Add(lv);
            if (ranked)
            {
                var rank = new VisualElement(); rank.AddToClassList("nxl-pb-rank");
                var emb = new Label("?"); emb.AddToClassList("nxl-rank-emblem-sm"); emb.style.width = 36; emb.style.height = 36;
                var rt = new Label(tier); rt.AddToClassList("nxl-pb-sub"); rt.style.marginLeft = 8;
                rank.Add(emb); rank.Add(rt); card.Add(rank);
            }
            // Ping: hanya bila terhubung Photon; di room belum terhubung → tidak ditampilkan.
            return BannerWrap(card);
        }

        private VisualElement OpponentBanner()
        {
            var card = new VisualElement(); card.AddToClassList("nxl-pbanner"); card.AddToClassList("nxl-pbanner--empty");
            var q = new Label("?"); q.AddToClassList("nxl-pb-q");
            var wait = new Label("Menunggu lawan"); wait.AddToClassList("nxl-pb-wait");
            card.Add(q); card.Add(wait);
            _opponentCard = card;
            return BannerWrap(card);
        }

        private VisualElement EmptyBanner()
        {
            var card = new VisualElement(); card.AddToClassList("nxl-pbanner"); card.AddToClassList("nxl-pbanner--empty");
            var inv = new Button { text = "+ Undang\nSegera Hadir" };
            inv.AddToClassList("nxl-btn"); inv.AddToClassList("nxl-btn--gray"); inv.SetEnabled(false);
            inv.style.width = 200; inv.style.height = 60; inv.style.whiteSpace = WhiteSpace.Normal;
            card.Add(inv);
            return BannerWrap(card);
        }

        // ---- Ruang Ladder (Ranked Solo) ----
        private void FillLadder()
        {
            var b = game != null ? game.Backend : null;
            bool on = b != null && b.LoggedIn;
            string name = PlayerName(b);
            // Tier SELALU dari backend (TierFor rating) — TIDAK ada placement.
            string tier = on ? (!string.IsNullOrEmpty(b.RankTier) ? b.RankTier : NexusRankConfig.TierForRating(b.Rating)) : "—";
            int w = on ? b.Wins : 0, l = on ? b.Losses : 0;
            string wr = on ? b.WinRate.ToString("0.#") : "0";
            // Faction hanya ORDE/LEGION. Kalau kosong → jangan tampilkan "NEXUS", cukup "RANKED SOLO".
            string fac = (on && b.Faction == 1) ? "ORDE" : (on && b.Faction == 2) ? "LEGION" : "";
            SetText("ld-winhead", fac.Length > 0 ? fac + " | RANKED SOLO" : "RANKED SOLO");
            SetText("ld-avatar", name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "N");
            SetText("ld-pname", name);
            SetText("ld-ptier", tier);

            SetText("ld-tier", tier);
            SetEmblem(_root.Q("ld-rank-emblem"), on ? tier : "");
            float p = on ? NexusRankConfig.Progress(tier, b.Rating) : -1f;
            ShowEl(_root.Q("ld-rank-track"), p >= 0f);
            var fill = _root.Q("ld-rank-fill"); if (fill != null) fill.style.width = Length.Percent(p >= 0f ? p * 100f : 0f);
            SetText("ld-rank-next", on ? NextTierText(tier, b.Rating) : "");

            SetText("ld-winrate", wr + "%");
            SetText("ld-wr-sub", w + " Menang · " + l + " Kalah");

            SetText("ld-third-big", on ? b.Kda.ToString("0.00") : "0.00");
            SetText("ld-third-sub", on ? ("K " + b.Kills + " · D " + b.Deaths + " · A " + b.Assists) : "K 0 · D 0 · A 0");
        }

        private async void FetchLadderData()
        {
            var b = game != null ? game.Backend : null; if (b == null) return;
            var matches = await b.GetMatchHistoryAsync();
            if (_partySolo) { FillMatches(matches); BuildGraph(matches); }
            var board = await b.GetLeaderboardAsync(100);
            var myRank = await b.GetMyRankAsync();
            if (_partySolo) FillTop(board, myRank);
        }

        private void FillMatches(NexusBackend.MatchRow[] rows)
        {
            var log = _root.Q<ScrollView>("ld-matches"); if (log == null) return;
            log.Clear();
            if (rows == null || rows.Length == 0) { log.Add(Dim("Belum ada pertandingan selesai.")); return; }
            foreach (var mr in rows)
            {
                var row = new VisualElement(); row.AddToClassList("nxl-ladder-row");
                row.AddToClassList(mr.won ? "nxl-ladder-row--win" : "nxl-ladder-row--loss");
                row.style.flexDirection = FlexDirection.Column; row.style.minHeight = 54;

                var l1 = new VisualElement(); l1.style.flexDirection = FlexDirection.Row;
                var res = new Label(mr.won ? "MENANG" : "KALAH"); res.AddToClassList("nxl-ladder-rowtxt"); res.style.width = 86;
                string mode = string.IsNullOrEmpty(mr.mode) ? "" : mr.mode.ToUpperInvariant();
                var hero = new Label(HeroName(mr.heroId) + (mode.Length > 0 ? "  ·  " + mode : "")); hero.AddToClassList("nxl-ladder-rowdim"); hero.style.flexGrow = 1;
                var tm = new Label(ShortTime(mr.endedAt)); tm.AddToClassList("nxl-ladder-rowdim");
                l1.Add(res); l1.Add(hero); l1.Add(tm);

                var mine = FindPlayer(mr, mr.slot, true);
                var opp = FindPlayer(mr, mr.slot, false);
                var l2 = new VisualElement(); l2.style.flexDirection = FlexDirection.Row; l2.style.marginTop = 2;
                var vs = new Label("vs " + (opp != null && !string.IsNullOrEmpty(opp.displayName) ? opp.displayName : "-")); vs.AddToClassList("nxl-ladder-rowdim"); vs.style.flexGrow = 1;
                var k = new Label(mine != null ? ("K" + mine.kills + " / D" + mine.deaths + " / A" + mine.assists) : ""); k.AddToClassList("nxl-ladder-rowdim"); k.style.width = 130;
                l2.Add(vs); l2.Add(k);
                if ((mr.mode ?? "").Trim().ToLowerInvariant() == "ranked") { string d = mr.ratingDelta >= 0 ? "+" + mr.ratingDelta : mr.ratingDelta.ToString(); var dl = new Label(d); dl.AddToClassList("nxl-ladder-rowdim"); dl.style.width = 60; l2.Add(dl); }

                row.Add(l1); row.Add(l2);
                log.Add(row);
            }
        }

        private void FillTop(NexusBackend.LeaderRow[] rows, NexusBackend.MyRankRes myRank)
        {
            var log = _root.Q<ScrollView>("ld-top"); if (log == null) return;
            log.Clear();
            var b = game != null ? game.Backend : null;
            string me = b != null ? (!string.IsNullOrEmpty(b.DisplayName) ? b.DisplayName : b.Username) : "";

            if (rows == null || rows.Length == 0)
                log.Add(Dim("Belum ada pemain yang selesai placement (min 5 match ranked)."));

            bool meShown = false;
            if (rows != null)
                for (int i = 0; i < rows.Length; i++)
                {
                    var r = rows[i];
                    bool mine = !string.IsNullOrEmpty(me) && r.displayName == me;
                    if (mine) meShown = true;
                    log.Add(TopRow("#" + r.rank, r.displayName, r.tier, r.ladderPoints, r.wins, r.losses, r.winRate, mine));
                }

            // Baris pemain sendiri di bawah: peringkat asli (kalau di luar daftar) / status placement.
            if (myRank != null && !meShown)
            {
                if (myRank.placed)
                    log.Add(TopRow("#" + myRank.rank, me, myRank.tier, myRank.ladderPoints, myRank.wins, myRank.losses, myRank.winRate, true));
                else
                {
                    var note = Dim(myRank.needed > 0
                        ? $"Selesaikan {myRank.needed} match ranked lagi untuk masuk peringkat."
                        : "Main match ranked untuk masuk peringkat.");
                    note.style.marginTop = 6;
                    log.Add(note);
                }
            }
        }

        private VisualElement TopRow(string rank, string name, string tier, int points, int w, int l, float winRate, bool mine)
        {
            var row = new VisualElement(); row.AddToClassList("nxl-ladder-row"); row.style.minHeight = 40;
            if (mine) row.AddToClassList("nxl-ladder-row--me");
            var rk = new Label(rank); rk.AddToClassList("nxl-ladder-rowtxt"); rk.style.width = 52;
            var em = new VisualElement(); em.AddToClassList("nxl-emblem"); SetEmblem(em, tier); em.style.width = 26; em.style.height = 26; em.style.flexShrink = 0;
            var nm = new Label(string.IsNullOrEmpty(name) ? "-" : name); nm.AddToClassList("nxl-ladder-rowtxt"); nm.style.flexGrow = 1; nm.style.marginLeft = 8;
            var tp = new Label(tier + " · " + points); tp.AddToClassList("nxl-ladder-rowdim"); tp.style.width = 150;
            var wl = new Label("W" + w + " / L" + l); wl.AddToClassList("nxl-ladder-rowdim"); wl.style.width = 92;
            var wr = new Label(winRate.ToString("0.#") + "%"); wr.AddToClassList("nxl-ladder-rowdim"); wr.style.width = 56;
            row.Add(rk); row.Add(em); row.Add(nm); row.Add(tp); row.Add(wl); row.Add(wr);
            return row;
        }

        // ---- Grafik Ranking (Painter2D) ----
        private void BuildGraph(NexusBackend.MatchRow[] rows)
        {
            if (_graphArea == null) return;
            var b = game != null ? game.Backend : null;
            int n = rows != null ? rows.Length : 0;
            bool ok = b != null && n >= 2;
            ShowEl(_graphEmpty, !ok);
            ShowEl(_graphCanvas, ok);
            ShowEl(_graphDates, ok);
            _graphArea.Query<Label>(className: "nxl-graph-lbl").ForEach(e => e.RemoveFromHierarchy()); // bersihkan label lama
            if (_graphDates != null) _graphDates.Clear();
            if (!ok) { _graphR = null; if (_graphCanvas != null) _graphCanvas.MarkDirtyRepaint(); return; }

            // rating kronologis (lama→baru); terakhir = rating sekarang (rows[] newest-first dari backend)
            float[] newestFirst = new float[n]; float s = 0f;
            for (int k = 0; k < n; k++) { newestFirst[k] = b.Rating - s; s += rows[k].ratingDelta; }
            var r = new float[n];
            for (int j = 0; j < n; j++) r[j] = newestFirst[n - 1 - j];
            _graphR = r;

            AddTierLabel("Conqueror", 1900f);
            AddTierLabel("Gladiator", 1700f);
            AddTierLabel("Rival", 1500f);
            AddTierLabel("Contender", 1300f);

            var now = new Label("Saat Ini"); now.AddToClassList("nxl-graph-lbl"); now.AddToClassList("nxl-graph-now");
            now.style.position = Position.Absolute; now.style.right = 8; now.style.top = Length.Percent(RatingToPct(r[n - 1]));
            _graphArea.Add(now);

            _graphDates.Add(DateLbl(ShortTime(rows[n - 1].endedAt)));
            if (n > 2) _graphDates.Add(DateLbl(ShortTime(rows[(n - 1) / 2].endedAt)));
            _graphDates.Add(DateLbl(ShortTime(rows[0].endedAt)));

            if (_graphCanvas != null) _graphCanvas.MarkDirtyRepaint();
        }

        private void AddTierLabel(string name, float centerRating)
        {
            var l = new Label(name); l.AddToClassList("nxl-graph-lbl"); l.AddToClassList("nxl-graph-tier");
            l.style.position = Position.Absolute; l.style.left = 6; l.style.top = Length.Percent(RatingToPct(centerRating));
            _graphArea.Add(l);
        }

        private Label DateLbl(string t) { var l = new Label(t); l.AddToClassList("nxl-ladder-rowdim"); return l; }
        private static float RatingToPct(float rating) { const float lo = 1200f, hi = 2000f; return Mathf.Clamp01(1f - (rating - lo) / (hi - lo)) * 100f; }

        private void OnDrawGraph(MeshGenerationContext ctx)
        {
            var r = _graphR;
            if (r == null || r.Length < 2) return;
            var rect = ctx.visualElement.contentRect;
            float W = rect.width, H = rect.height;
            if (W < 5f || H < 5f) return;
            const float lo = 1200f, hi = 2000f;
            float padL = 76f, padR = 24f, padT = 12f, padB = 12f;
            float gw = W - padL - padR, gh = H - padT - padB;
            if (gw < 5f || gh < 5f) return;
            var p = ctx.painter2D;

            var bands = NexusRankConfig.Bands;
            for (int i = 0; i < bands.Length; i++)
            {
                var bnd = bands[i];
                if (bnd.Max <= lo || bnd.Min >= hi) continue;
                float mn = Mathf.Max(bnd.Min, lo), mx = Mathf.Min(bnd.Max, hi);
                float y0 = padT + gh * (1f - (mx - lo) / (hi - lo));
                float y1 = padT + gh * (1f - (mn - lo) / (hi - lo));
                // band tier = emas redup (-dim), bukan cyan
                p.fillColor = (i % 2 == 0) ? new Color(0.961f, 0.773f, 0.259f, 0.05f) : new Color(0.961f, 0.773f, 0.259f, 0.085f);
                p.BeginPath();
                p.MoveTo(new Vector2(padL, y0)); p.LineTo(new Vector2(padL + gw, y0));
                p.LineTo(new Vector2(padL + gw, y1)); p.LineTo(new Vector2(padL, y1)); p.ClosePath(); p.Fill();
            }

            int n = r.Length;
            p.strokeColor = new Color(0.251f, 0.588f, 1f, 1f); p.lineWidth = 3f; // garis data = biru (kamu/ally)
            p.BeginPath();
            for (int i = 0; i < n; i++)
            {
                float x = padL + gw * (float)i / (n - 1);
                float y = padT + gh * (1f - (Mathf.Clamp(r[i], lo, hi) - lo) / (hi - lo));
                if (i == 0) p.MoveTo(new Vector2(x, y)); else p.LineTo(new Vector2(x, y));
            }
            p.Stroke();

            for (int i = 0; i < n; i++)
            {
                float x = padL + gw * (float)i / (n - 1);
                float y = padT + gh * (1f - (Mathf.Clamp(r[i], lo, hi) - lo) / (hi - lo));
                float rad = (i == n - 1) ? 7f : 3.5f;
                // titik terakhir = sekarang/fokus → cyan; lainnya = biru (ally)
                p.fillColor = (i == n - 1) ? new Color(0.31f, 0.82f, 1f, 1f) : new Color(0.251f, 0.588f, 1f, 1f);
                p.BeginPath(); p.Arc(new Vector2(x, y), rad, Angle.Degrees(0f), Angle.Degrees(360f)); p.Fill();
            }
        }

        private void ShowLadderTab(int i)
        {
            ShowEl(_root.Q("ld-matches"), i == 0);
            ShowEl(_root.Q("ld-top"), i == 1);
            ShowEl(_root.Q("ld-graph"), i == 2);
            Active2("ltab-matches", i == 0); Active2("ltab-top", i == 1); Active2("ltab-graph", i == 2);
        }

        private void BuildVs()
        {
            if (_vsSlots == null) return;
            _vsSlots.Clear();
            _vsSlots.Add(PlayerBanner(true));
            var vs = new Label("VS"); vs.AddToClassList("nxl-vs"); _vsSlots.Add(vs);
            var card = new VisualElement(); card.AddToClassList("nxl-pbanner"); card.AddToClassList("nxl-pbanner--empty");
            var t = new Label("LAWAN"); t.AddToClassList("nxl-pb-name"); t.style.marginTop = 130;
            var s = new Label("Ditemukan"); s.AddToClassList("nxl-pb-sub");
            card.Add(t); card.Add(s);
            _vsSlots.Add(BannerWrap(card));
        }

        private void SendLadderChat()
        {
            if (_ldChatInput == null || _ldChatLog == null) return;
            var t = _ldChatInput.value != null ? _ldChatInput.value.Trim() : "";
            if (t.Length == 0) return;
            var who = game != null && game.Backend != null && game.Backend.LoggedIn ? PlayerName(game.Backend) : "Kamu";
            var line = new Label(who + ": " + t); line.style.whiteSpace = WhiteSpace.Normal; line.AddToClassList("nxl-dim");
            _ldChatLog.Add(line); _ldChatInput.value = ""; _ldChatInput.Focus();
        }

        // Sumber nama TUNGGAL: displayName (sama dengan leaderboard), fallback username.
        public static string PlayerName(NexusBackend b)
            => b == null ? "Runner" : (!string.IsNullOrEmpty(b.DisplayName) ? b.DisplayName : (!string.IsNullOrEmpty(b.Username) ? b.Username : "Runner"));

        public static string Initial(string name) => string.IsNullOrEmpty(name) ? "N" : name.Substring(0, 1).ToUpperInvariant();

        private static readonly string[] EmblemTiers = { "rookie", "contender", "rival", "gladiator", "conqueror", "nexuslegend" };
        public static string TierKey(string tier)
        {
            if (string.IsNullOrEmpty(tier)) return "rookie";
            string t = tier.ToLowerInvariant();
            if (t.Contains("nexus")) return "nexuslegend";
            if (t.Contains("conqueror")) return "conqueror";
            if (t.Contains("gladiator")) return "gladiator";
            if (t.Contains("rival")) return "rival";
            if (t.Contains("contender")) return "contender";
            return "rookie";
        }
        public static void SetEmblem(VisualElement e, string tier)
        {
            if (e == null) return;
            string key = TierKey(tier);
            foreach (var t in EmblemTiers) e.EnableInClassList("nxl-emblem--" + t, t == key);
        }

        private static readonly string[] TierDisplay = { "Rookie", "Contender", "Rival", "Gladiator", "Conqueror", "Nexus Legend" };
        public static string NextTierText(string tier, int rating)
        {
            int idx = System.Array.IndexOf(EmblemTiers, TierKey(tier));
            if (idx < 0) return "";
            if (idx >= EmblemTiers.Length - 1) return "Tier tertinggi";
            int pct = Mathf.RoundToInt(Mathf.Clamp01(NexusRankConfig.Progress(tier, rating)) * 100f);
            return pct + "% menuju " + TierDisplay[idx + 1];
        }
        public static string HeroName(int heroId) => heroId == 0 ? "TITAN-01" : "PULSE-X";
        private static NexusBackend.MatchPlayerRow FindPlayer(NexusBackend.MatchRow m, int slot, bool same)
        {
            if (m.players == null) return null;
            foreach (var p in m.players) if ((p.slot == slot) == same) return p;
            return null;
        }

        private void SetText(string n, string v) { var l = _root.Q<Label>(n); if (l != null) l.text = v; }
        private void Active2(string n, bool v) { var e = _root.Q(n); if (e != null) e.EnableInClassList("nxl-tab--active", v); }
        private void ClickEl(string n, System.Action cb) { var e = _root.Q(n); if (e != null) e.RegisterCallback<ClickEvent>(_ => cb()); }
        private static Label Dim(string t) { var l = new Label(t); l.AddToClassList("nxl-dim"); l.style.whiteSpace = WhiteSpace.Normal; return l; }
        private static string ShortTime(string iso) => string.IsNullOrEmpty(iso) ? "" : (iso.Length >= 10 ? iso.Substring(0, 10) : iso);

        private void ApplyMode()
        {
            var m = _modes[_mode];
            if (_modeTitle != null) _modeTitle.text = m.Title;
            if (_modeDesc != null) _modeDesc.text = m.Desc;
            if (_primary != null) { _primary.text = m.Disabled ? m.Primary : "MAIN"; _primary.SetEnabled(!m.Disabled); } // teaser: nonaktif + SEGERA HADIR
            ShowEl(_createRoom, false);            // Create Room pindah ke Ruang Tim
            ShowEl(_rankMini, m.Ranked);          // rank kontekstual hanya mode Ranked
            ShowEl(_modeDesc, !m.Ranked);         // selain Ranked: deskripsi mode
            if (_dots != null)
                for (int i = 0; i < _dots.Length; i++)
                {
                    if (_dots[i] == null) continue;
                    ShowEl(_dots[i], i < _modes.Length);
                    _dots[i].EnableInClassList("nxl-dot--active", i == _mode);
                }
        }

        private void RefreshAccount()
        {
            var b = game != null ? game.Backend : null;
            bool on = b != null && b.LoggedIn;
            string name = PlayerName(b);
            if (_name != null) _name.text = name;
            SetFactionBadge(_root.Q<Label>("player-faction"), on ? b.Faction : 0); // badge faksi (docs/35)
            SetText("avatar", Initial(name)); // inisial dari display name
            if (_level != null) _level.text = on ? "Lv " + b.AccountLevel : "Lv -";
            string tier = on ? (!string.IsNullOrEmpty(b.RankTier) ? b.RankTier : NexusRankConfig.TierForRating(b.Rating)) : "UNRANKED";
            if (_rankTier != null) _rankTier.text = tier;
            if (_rankSub != null) _rankSub.text = on ? ("Menang " + b.Wins + " · Kalah " + b.Losses) : "Belum ada data";
            float rp = on ? NexusRankConfig.Progress(tier, b.Rating) : -1f; // tier + bar saja, TANPA angka MMR
            ShowEl(_root.Q("rank-mini-track"), rp >= 0f);
            var rf = _root.Q("rank-mini-fill"); if (rf != null) rf.style.width = Length.Percent(rp >= 0f ? rp * 100f : 0f);
            SetEmblem(_root.Q("rank-mini-emblem"), on ? tier : "");
            // Currency (Gold/Ticket/Diamond) tetap 0 — belum ada backend ekonomi (tanpa dummy).
        }

        // Badge faksi konsisten (docs/35): ORDE (biru) / LEGION (merah) / sembunyi bila belum pilih.
        public static void SetFactionBadge(Label el, int faction)
        {
            if (el == null) return;
            el.EnableInClassList("nxl-facbadge--orde", faction == 1);
            el.EnableInClassList("nxl-facbadge--legion", faction == 2);
            el.EnableInClassList("nxl-facbadge--none", faction != 1 && faction != 2);
            el.text = faction == 1 ? "ORDE" : faction == 2 ? "LEGION" : "";
        }

        private void SendChat()
        {
            if (_chatInput == null || _chatLog == null) return;
            var t = _chatInput.value != null ? _chatInput.value.Trim() : "";
            if (t.Length == 0) return;
            var who = game != null && game.Backend != null && game.Backend.LoggedIn ? PlayerName(game.Backend) : "Kamu";
            var line = new Label(who + ": " + t); line.style.whiteSpace = WhiteSpace.Normal; line.AddToClassList("nxl-dim");
            _chatLog.Add(line);
            _chatInput.value = "";
            _chatInput.Focus();
        }

        private void Click(string name, Action cb)
        {
            var b = _root.Q<Button>(name);
            if (b != null) b.clicked += cb;
        }

        private static void ShowEl(VisualElement e, bool v)
        {
            if (e != null) e.style.display = v ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
