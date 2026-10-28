using UnityEngine;
using UnityEngine.UIElements;

namespace Nexus
{
    /// <summary>
    /// Tahap B — halaman Profil UI Toolkit (ProfilePage.uxml, UIDocument sendiri di atas lobby).
    /// Tampil saat NexusGame.IsProfile; Back/Esc kembali ke lobby. Sidebar 4 tab (Tab Profil berisi data,
    /// Statistik/Hero/Riwayat masih stub Tahap C/D/E). TANPA dummy: field dari NexusBackend, sisanya empty state.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ProfileController : MonoBehaviour
    {
        public NexusGame game;
        public UIDocument document;

        private VisualElement _root, _profileRoot, _tabMain, _tabStats, _tabHeroes, _tabHistory;
        private Button _navMain, _navStats, _navHeroes, _navHistory;
        private bool _bound;
        private float _nextFill;

        private void Awake()
        {
            if (document == null) document = GetComponent<UIDocument>();
            if (game == null) game = FindObjectOfType<NexusGame>();
        }

        private void OnEnable() { _bound = false; TryBind(); }
        private void OnDisable() { _bound = false; }

        private void TryBind()
        {
            if (_bound || document == null) return;
            _root = document.rootVisualElement;
            if (_root == null) return;
            _root.pickingMode = PickingMode.Ignore; // container tak memblok klik lobby saat profil tersembunyi
            _profileRoot = _root.Q("profile-root");

            _tabMain = _root.Q("tab-main"); _tabStats = _root.Q("tab-stats");
            _tabHeroes = _root.Q("tab-heroes"); _tabHistory = _root.Q("tab-history");
            _navMain = _root.Q<Button>("ptab-main"); _navStats = _root.Q<Button>("ptab-stats");
            _navHeroes = _root.Q<Button>("ptab-heroes"); _navHistory = _root.Q<Button>("ptab-history");

            Click("profile-back", Close);
            if (_navMain != null) _navMain.clicked += () => ShowTab(0);
            if (_navStats != null) _navStats.clicked += () => ShowTab(1);
            if (_navHeroes != null) _navHeroes.clicked += () => ShowTab(2);
            if (_navHistory != null) _navHistory.clicked += () => ShowTab(3);
            Click("prof-copy", CopyId);
            Click("stats-play", Close); Click("heroes-play", Close); Click("history-play", Close);

            ShowTab(0);
            _bound = true;
        }

        private void Update()
        {
            if (!_bound) { TryBind(); if (!_bound) return; }
            if (game == null) return;

            bool show = game.IsProfile;
            if (_profileRoot != null) _profileRoot.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;

            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (Time.unscaledTime >= _nextFill) { Fill(); _nextFill = Time.unscaledTime + 0.5f; }
        }

        private void Close() { if (game != null) game.UiProfileBack(); }

        private void ShowTab(int i)
        {
            ShowEl(_tabMain, i == 0); ShowEl(_tabStats, i == 1); ShowEl(_tabHeroes, i == 2); ShowEl(_tabHistory, i == 3);
            Active(_navMain, i == 0); Active(_navStats, i == 1); Active(_navHeroes, i == 2); Active(_navHistory, i == 3);
        }

        private void Fill()
        {
            var b = game.Backend;
            if (b == null || !b.LoggedIn) return;

            string name = NexusLobbyController.PlayerName(b);
            SetText("prof-name", name);
            SetText("prof-id", "ID: " + b.PlayerId);
            SetText("prof-level", "Lv " + b.AccountLevel);
            SetText("prof-xp", "XP " + b.AccountXp);

            var fac = _root.Q<Label>("prof-faction");
            if (fac != null) { string f = b.Faction == 1 ? "ORDE" : b.Faction == 2 ? "LEGION" : ""; fac.text = f; ShowEl(fac, f.Length > 0); }

            bool ranked = !string.IsNullOrEmpty(b.RankTier);
            SetText("prof-rank-tier", ranked ? b.RankTier : "UNRANKED");
            SetText("prof-rank-sub", ranked ? "" : "Selesaikan 5 placement");
            float p = NexusRankConfig.Progress(b.RankTier, b.Rating); // bar saja, tanpa angka MMR
            ShowEl(_root.Q("prof-rank-track"), p >= 0f);
            var fill = _root.Q("prof-rank-progress");
            if (fill != null) fill.style.width = Length.Percent(p >= 0f ? p * 100f : 0f);

            SetText("prof-matches", b.MatchesPlayed.ToString());
            SetText("prof-winrate", b.WinRate.ToString("0.#") + "%");
        }

        private void CopyId()
        {
            if (game != null && game.Backend != null) GUIUtility.systemCopyBuffer = game.Backend.PlayerId;
        }

        private void SetText(string n, string v) { var l = _root.Q<Label>(n); if (l != null) l.text = v; }
        private void Active(Button b, bool v) { if (b != null) b.EnableInClassList("nxl-prof-navitem--active", v); }
        private void Click(string n, System.Action cb) { var b = _root.Q<Button>(n); if (b != null) b.clicked += cb; }
        private static void ShowEl(VisualElement e, bool v) { if (e != null) e.style.display = v ? DisplayStyle.Flex : DisplayStyle.None; }
    }
}
