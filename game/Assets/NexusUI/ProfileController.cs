using System.Collections.Generic;
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

        // Statistik (Tab 1.3)
        private VisualElement _statsContent, _statsEmpty, _wrCanvas, _rankCanvas;
        private NexusBackend.MatchRow[] _statMatches;
        private float[] _wrSeries, _rankSeries;
        private int _statsFilter;   // 0 Semua, 1 Ranked, 2 Battle
        private bool _statsFetched;

        // Riwayat (Tab 1.5) — kartu gaya ML + detail scoreboard
        private ScrollView _histList; private VisualElement _histEmpty, _histDetail; private VisualElement _histLeft, _histRight;
        private NexusBackend.MatchRow[] _histMatches;
        private int _histFilter; private bool _histFetched; private bool _detailOpen;
        public enum DetailMode { History, Result }   // History = dari Riwayat; Result = layar hasil pasca-match (wiring NexusGame = poin e)
        private DetailMode _detailMode = DetailMode.History;

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

            _statsContent = _root.Q("stats-content");
            _statsEmpty = _root.Q("stats-empty");
            _wrCanvas = _root.Q("st-wr-canvas");
            _rankCanvas = _root.Q("st-rank-canvas");
            if (_wrCanvas != null) _wrCanvas.generateVisualContent += OnDrawWr;
            if (_rankCanvas != null) _rankCanvas.generateVisualContent += OnDrawRank;
            ClickEl("sf-all", () => SetStatsFilter(0));
            ClickEl("sf-ranked", () => SetStatsFilter(1));
            ClickEl("sf-battle", () => SetStatsFilter(2));

            _histList = _root.Q<ScrollView>("hist-list");
            _histEmpty = _root.Q("history-empty");
            _histDetail = _root.Q("hist-detail");
            // Overlay Detail Match = layar penuh: pindahkan ke profile-root agar menutupi sidebar + konten (sembunyikan sidebar & tombol Back profil).
            if (_histDetail != null && _profileRoot != null) { _histDetail.RemoveFromHierarchy(); _profileRoot.Add(_histDetail); }
            _histLeft = _root.Q("hist-left"); _histRight = _root.Q("hist-right");
            if (_histList != null) _histList.RegisterCallback<GeometryChangedEvent>(_ => UpdateArrows());
            ClickEl("hf-all", () => SetHistFilter(0));
            ClickEl("hf-ranked", () => SetHistFilter(1));
            ClickEl("hf-battle", () => SetHistFilter(2));
            ClickEl("hist-left", () => ScrollHist(-1));
            ClickEl("hist-right", () => ScrollHist(1));

            ShowTab(0);
            _bound = true;
        }

        private void Update()
        {
            if (!_bound) { TryBind(); if (!_bound) return; }
            if (game == null) return;

            bool result = game.IsResult;                       // NX-14: layar hasil pasca-match
            bool show = game.IsProfile || result;
            if (_profileRoot != null) _profileRoot.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) { _statsFetched = false; _histFetched = false; _resultFlow = false; return; }
            if (result)
            {
                if (!_resultFlow) { _resultFlow = true; ShowResult(); }
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return)) ContinueFromResult();
                return;
            }
            _resultFlow = false;

            if (Input.GetKeyDown(KeyCode.Escape)) { if (_detailOpen) { CloseDetail(); return; } Close(); return; }
            if (Time.unscaledTime >= _nextFill) { Fill(); _nextFill = Time.unscaledTime + 0.5f; }
        }

        private void Close() { if (game != null) game.UiProfileBack(); }

        private void ShowTab(int i)
        {
            ShowEl(_tabMain, i == 0); ShowEl(_tabStats, i == 1); ShowEl(_tabHeroes, i == 2); ShowEl(_tabHistory, i == 3);
            Active(_navMain, i == 0); Active(_navStats, i == 1); Active(_navHeroes, i == 2); Active(_navHistory, i == 3);
            if (i == 1) EnsureStats();
            if (i == 2 || i == 0) FetchHeroes(); // NX-22 (tab Hero Favorit + hero teratas di tab Profil)
            if (i == 3) EnsureHistory();
            if (i != 3) CloseDetail(); // tutup detail saat pindah tab
        }

        private void CloseDetail() { if (_histDetail != null) ShowEl(_histDetail, false); _detailOpen = false; }

        // ---- NX-14: layar hasil = Detail Match v2 mode Result ----
        private bool _resultFlow;

        private void ContinueFromResult()
        {
            CloseDetail();
            if (game != null) game.UiResultContinue(); // → lobby (lalu layar faksi bila match pertama & belum berfaksi, NX-15)
        }

        // Cari match yang baru selesai di riwayat. Riwayat hanya berisi match Finished → bila lawan belum melapor,
        // coba lagi tiap 1 dtk (maks ~15 dtk). Tak ketemu → ringkasan teks jujur (belum tercatat / tidak dihitung).
        private async void ShowResult()
        {
            if (game.ResultIsPractice && game.ResultLocalRow != null) { OpenDetail(game.ResultLocalRow, DetailMode.Result); return; } // NX: latihan -> template Result lokal
            ResultPlaceholder("MEMUAT HASIL...", game.ResultText, false);
            var b = game.Backend;
            string gid = game.ResultGameId;
            for (int i = 0; i < 15 && b != null && !string.IsNullOrEmpty(gid); i++)
            {
                var rows = await b.GetMatchHistoryAsync(includeDisputed: true); // layar hasil: Disputed → "TIDAK DIHITUNG"
                if (!game.IsResult) return;                                  // pemain sudah lanjut
                if (rows != null)
                    foreach (var r in rows)
                        if (r.gameId == gid) { OpenDetail(r, DetailMode.Result); return; }
                await System.Threading.Tasks.Task.Delay(1000);
                if (!game.IsResult) return;
            }
            ResultPlaceholder("HASIL MATCH", game.ResultText,
                true, "Rincian belum tersedia: hasil belum tercatat (lawan belum melapor, tidak dihitung, atau server tak terjangkau). Cek Riwayat nanti.");
        }

        private void ResultPlaceholder(string title, string body, bool canContinue, string note = null)
        {
            if (_histDetail == null) return;
            _detailMode = DetailMode.Result;
            _histDetail.Clear();
            var banner = new VisualElement(); banner.AddToClassList("nxl-dtx-banner");
            var t = new Label(title); t.AddToClassList("nxl-dtx-res"); banner.Add(t);
            if (!string.IsNullOrEmpty(body)) { var bd = new Label(body); bd.AddToClassList("nxl-dtx-meta"); bd.style.whiteSpace = WhiteSpace.Normal; banner.Add(bd); }
            if (!string.IsNullOrEmpty(note)) { var n = new Label(note); n.AddToClassList("nxl-dtx-meta"); n.style.whiteSpace = WhiteSpace.Normal; n.style.marginTop = 16; banner.Add(n); }
            _histDetail.Add(banner);
            if (canContinue)
            {
                var foot = new VisualElement(); foot.AddToClassList("nxl-dtx-foot");
                var sp = new VisualElement(); sp.style.flexGrow = 1; foot.Add(sp);
                var cta = new Button(ContinueFromResult) { text = "LANJUT" };
                cta.AddToClassList("nxl-btn"); cta.AddToClassList("nxl-btn--orange"); cta.style.width = 180; cta.style.height = 46;
                foot.Add(cta); _histDetail.Add(foot);
            }
            ShowEl(_histDetail, true); _detailOpen = true;
        }

        private void Fill()
        {
            var b = game.Backend;
            if (b == null || !b.LoggedIn) return;

            string name = NexusLobbyController.PlayerName(b);
            SetText("prof-name", name);
            SetText("prof-avatar", NexusLobbyController.Initial(name));
            SetText("prof-id", "ID: " + b.PlayerId);
            SetText("prof-level", "Lv " + b.AccountLevel);
            SetText("prof-xp", "XP " + b.AccountXp);

            NexusLobbyController.SetFactionBadge(_root.Q<Label>("prof-faction"), b.Faction); // badge faksi konsisten (docs/35)

            // Tier SELALU dari backend (TierFor rating) — tanpa placement.
            string tier = !string.IsNullOrEmpty(b.RankTier) ? b.RankTier : NexusRankConfig.TierForRating(b.Rating);
            SetText("prof-rank-tier", tier);
            SetText("prof-rank-sub", "");
            NexusLobbyController.SetEmblem(_root.Q("prof-rank-emblem"), tier);
            float p = NexusRankConfig.Progress(tier, b.Rating); // bar saja, tanpa angka MMR
            ShowEl(_root.Q("prof-rank-track"), p >= 0f);
            var fill = _root.Q("prof-rank-progress");
            if (fill != null) fill.style.width = Length.Percent(p >= 0f ? p * 100f : 0f);

            SetText("prof-matches", b.MatchesPlayed.ToString());
            SetText("prof-winrate", b.WinRate.ToString("0.#") + "%");
        }

        // ================= Tab RIWAYAT (1.5) — kartu gaya ML + detail scoreboard =================
        private void SetHistFilter(int f)
        {
            _histFilter = f;
            ActiveTab("hf-all", f == 0); ActiveTab("hf-ranked", f == 1); ActiveTab("hf-battle", f == 2);
            RefreshHistory();
        }

        private void EnsureHistory() { RefreshHistory(); if (!_histFetched) FetchHistory(); }

        // ---- NX-22: Hero Favorit ----
        private bool _heroesBusy;
        private async void FetchHeroes()
        {
            var b = game != null ? game.Backend : null;
            if (b == null || _heroesBusy) return;
            _heroesBusy = true;
            var rows = await b.GetMyHeroesAsync();
            _heroesBusy = false;

            var grid = _root.Q<VisualElement>("heroes-grid");
            var empty = _root.Q<VisualElement>("heroes-empty");
            var msg = _root.Q<Label>("heroes-msg");
            bool has = rows != null && rows.Length > 0;
            ShowEl(grid, has); ShowEl(empty, rows != null && !has);
            if (msg != null) msg.text = rows == null ? "Gagal memuat hero favorit. Coba buka tab ini lagi." : "";
            if (grid != null && has)
            {
                grid.Clear();
                foreach (var h in rows) grid.Add(HeroCard(h));
            }
            var fav = _root.Q<Label>("fav-hero-text");
            if (fav != null)
                fav.text = has ? $"{HeroName(rows[0].heroId)} — {rows[0].matches} match · Win rate {rows[0].winRate:0.#}%"
                               : rows == null ? "Gagal memuat." : "Hero favorit akan muncul setelah kamu bermain.";
        }

        private VisualElement HeroCard(NexusBackend.HeroAggRow h)
        {
            var card = new VisualElement(); card.AddToClassList("nxl-herocard");
            var art = new VisualElement(); art.AddToClassList("nxl-herocard-art"); art.AddToClassList(HeroCss(h.heroId)); card.Add(art);
            var name = new Label(HeroName(h.heroId)); name.AddToClassList("nxl-herocard-name"); card.Add(name);
            card.Add(HeroStat("Match", h.matches.ToString()));
            card.Add(HeroStat("Menang / Kalah", h.draws > 0 ? $"{h.wins} / {h.losses} · {h.draws} seri" : $"{h.wins} / {h.losses}"));
            card.Add(HeroStat("Win rate", h.wins + h.losses > 0 ? h.winRate.ToString("0.#") + "%" : "–"));
            card.Add(HeroStat("K / D", h.kdMatches > 0 ? $"{h.kills} / {h.deaths}  (KDA {h.kda:0.00})" : "–"));   // K/D hanya ranked 1v1
            card.Add(HeroStat("Waktu main", h.playMinutes > 0 ? h.playMinutes.ToString("0.#") + " mnt" : "–"));
            return card;
        }

        private static VisualElement HeroStat(string k, string v)
        {
            var row = new VisualElement(); row.AddToClassList("nxl-herocard-row");
            var a = new Label(k); a.AddToClassList("nxl-herocard-k"); row.Add(a);
            var b = new Label(v); b.AddToClassList("nxl-herocard-v"); row.Add(b);
            return row;
        }

        private async void FetchHistory()
        {
            var b = game != null ? game.Backend : null; if (b == null) return;
            _histMatches = await b.GetMatchHistoryAsync(includeDisputed: true); // NX-21: kartu Disputed ikut tampil
            _histFetched = true;
            RefreshHistory();
        }

        private void RefreshHistory()
        {
            if (_histList == null) return;
            var all = _histMatches ?? System.Array.Empty<NexusBackend.MatchRow>();
            var f = new List<NexusBackend.MatchRow>();
            foreach (var m in all)
            {
                var md = ModeOf(m);
                if (_histFilter == 0 || (_histFilter == 1 && md == "ranked") || (_histFilter == 2 && md == "battle")) f.Add(m);
            }
            bool empty = f.Count == 0;
            SetText("history-msg", _histFilter == 0 ? "Belum ada pertandingan selesai." : "Belum ada pertandingan di mode ini.");
            ShowEl(_histEmpty, empty); ShowEl(_root.Q("hist-wrap"), !empty);
            var sumLbl = _root.Q<Label>("hist-summary"); ShowEl(sumLbl, !empty);
            _histList.Clear();
            if (empty) return;

            int w = 0, l = 0, dr = 0, dsp = 0, ptsSum = 0;
            foreach (var m in f)
            {
                if (IsDisputed(m)) dsp++;                              // NX-21: tidak dihitung menang/kalah
                else if (m.isDraw) dr++; else if (m.won) w++; else l++;   // seri bukan kalah (T2)
                if (m.format == "ranked1v1") ptsSum += NexusRankConfig.LadderPointsGlobal(m.ratingBefore + m.ratingDelta) - NexusRankConfig.LadderPointsGlobal(m.ratingBefore);
                _histList.Add(BuildHistoryCard(m));
            }
            int tot = w + l + dr + dsp; int wr = w + l > 0 ? Mathf.RoundToInt(100f * w / (w + l)) : 0;
            string drTxt = (dr > 0 ? $" · {dr} Seri" : "") + (dsp > 0 ? $" · {dsp} Tidak dihitung" : "");
            if (sumLbl != null) sumLbl.text = $"{tot} match terakhir:   {w} Menang · {l} Kalah{drTxt} · Win rate {wr}% · Poin Ladder {(ptsSum >= 0 ? "+" : "")}{ptsSum}";
            UpdateArrows();
        }

        private Coroutine _scrollCo;
        private void ScrollHist(int dir)
        {
            if (_histList == null) return;
            float target = Mathf.Max(0f, _histList.scrollOffset.x + dir * 296f); // 280 kartu + 16 margin
            if (_scrollCo != null) StopCoroutine(_scrollCo);
            _scrollCo = StartCoroutine(SmoothScroll(target));
        }
        private System.Collections.IEnumerator SmoothScroll(float target)
        {
            float start = _histList.scrollOffset.x, t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / 0.18f;
                var o = _histList.scrollOffset; o.x = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t))); _histList.scrollOffset = o;
                UpdateArrows(); yield return null;
            }
            var fin = _histList.scrollOffset; fin.x = target; _histList.scrollOffset = fin; UpdateArrows();
        }
        private void UpdateArrows()
        {
            if (_histList == null || _histLeft == null || _histRight == null) return;
            float max = _histList.contentContainer.layout.width - _histList.layout.width;
            float x = _histList.scrollOffset.x;
            ShowEl(_histLeft, x > 2f);
            ShowEl(_histRight, max > 2f && x < max - 2f);
        }

        private VisualElement BuildHistoryCard(NexusBackend.MatchRow m)
        {
            bool ranked = ModeOf(m) == "ranked" && !IsDisputed(m); // Disputed: tanpa Poin Ladder (rating tak berubah)
            bool legacy = m.format == "legacy5v5";
            bool kdValid = m.format == "ranked1v1";
            var mine = Mine(m);
            NexusBackend.MatchPlayerRow opp = null;
            if (m.players != null) foreach (var p in m.players) if (p.slot != m.slot) { opp = p; break; }

            var card = new VisualElement(); card.AddToClassList("nxl-hist-card");
            string rk = ResultKey(m);
            card.AddToClassList("nxl-hist-card--" + rk);
            card.RegisterCallback<ClickEvent>(_ => OpenDetail(m));

            // Header band
            var head = new VisualElement(); head.AddToClassList("nxl-hist-head"); head.AddToClassList("nxl-hist-head--" + rk);
            var res = new Label(ResultText(m)); res.AddToClassList("nxl-hist-res"); res.AddToClassList("nxl-hist-res--" + rk);
            var hsp = new VisualElement(); hsp.style.flexGrow = 1;
            head.Add(res); head.Add(hsp);
            if (m.partySize > 1) head.Add(Badge("PARTY", "nxl-badge--party"));
            var mode = new Label(ModeLabel(m)); mode.AddToClassList("nxl-hist-mode"); head.Add(mode);
            card.Add(head);

            // Art hero (full-bleed)
            int dom = legacy ? -1 : m.heroId;
            var art = new VisualElement(); art.AddToClassList("nxl-hist-art");
            var hero = new VisualElement(); hero.AddToClassList("nxl-hist-hero"); hero.AddToClassList(HeroCss(dom)); art.Add(hero);
            if (rk == "loss") { var ds = new VisualElement(); ds.AddToClassList("nxl-hist-desat"); art.Add(ds); }
            if (!legacy && mine != null && mine.heroes != null && mine.heroes.Length > 1)
            {
                int second = -1; foreach (var h in mine.heroes) if (h.heroId != dom) { second = h.heroId; break; }
                if (second >= 0) { var alt = new VisualElement(); alt.AddToClassList("nxl-hist-heroalt"); alt.AddToClassList(HeroCss(second)); art.Add(alt); }
            }
            if (legacy) { var ov = new VisualElement(); ov.AddToClassList("nxl-hist-legacy-ov"); ov.Add(new Label("FORMAT LAMA")); art.Add(ov); }
            else { var banner = new Label(HeroName(dom)); banner.AddToClassList("nxl-hist-herobanner"); art.Add(banner); }
            card.Add(art);

            // Body
            var body = new VisualElement(); body.AddToClassList("nxl-hist-body");
            var score = new Label(m.scoreLeft + " - " + m.scoreRight); score.AddToClassList("nxl-hist-score"); body.Add(score);
            string oppName = opp != null && !string.IsNullOrEmpty(opp.displayName) ? opp.displayName : "-";
            var vs = new Label("vs " + oppName); vs.AddToClassList("nxl-hist-vs"); body.Add(vs);
            string endTxt = EndText(m);
            if (endTxt != null) { var et = new Label(endTxt); et.AddToClassList("nxl-hist-end"); body.Add(et); }

            if (ranked && !legacy)
            {
                int before = m.ratingBefore, after = m.ratingBefore + m.ratingDelta;
                int dG = NexusRankConfig.LadderPointsGlobal(after) - NexusRankConfig.LadderPointsGlobal(before); // GLOBAL → searah rating
                string tB = NexusRankConfig.TierForRating(before), tA = NexusRankConfig.TierForRating(after);
                var rank = new VisualElement(); rank.AddToClassList("nxl-hist-rank");
                var arrow = new Label(" → "); arrow.AddToClassList("nxl-dim");
                var pts = new Label((dG >= 0 ? "+" : "") + dG + " Poin"); pts.AddToClassList("nxl-hist-rankpts"); pts.AddToClassList(dG >= 0 ? "nxl-pts-up" : "nxl-pts-dn");
                rank.Add(Emblem(tB, 30)); rank.Add(arrow); rank.Add(Emblem(tA, 30)); rank.Add(pts); body.Add(rank);
                if (tA != tB) { var tl = new Label(after > before ? "NAIK TIER!" : "TURUN TIER"); tl.AddToClassList("nxl-hist-tierlbl"); tl.AddToClassList(after > before ? "nxl-hist-tierup" : "nxl-hist-tierdn"); body.Add(tl); }
            }

            if (!kdValid || mine == null) { var na = new Label("Detail tidak tersedia"); na.AddToClassList("nxl-kd-na"); body.Add(na); }
            else
            {
                var kd = new VisualElement(); kd.AddToClassList("nxl-hist-kd");
                kd.Add(KdIcon("nxl-kd-sword")); var kv = new Label(mine.kills.ToString()); kv.AddToClassList("nxl-kd-v"); kd.Add(kv);
                kd.Add(KdIcon("nxl-kd-skull")); var dv = new Label(mine.deaths.ToString()); dv.AddToClassList("nxl-kd-v"); kd.Add(dv);
                body.Add(kd);
            }

            var badges = new VisualElement(); badges.AddToClassList("nxl-badges");
            int loserScore = Mathf.Min(m.scoreLeft, m.scoreRight);
            // D13: badge hanya untuk ranked1v1 + battle (format lama tidak).
            if (!legacy && m.won && loserScore == 0 && KoFinish(m)) badges.Add(Badge("FLAWLESS", "nxl-badge--flawless"));
            if (!legacy && m.won && mine != null && mine.maxDeficit >= 3) badges.Add(Badge("COMEBACK", "nxl-badge--comeback"));
            if (mine != null && mine.firstBlood) badges.Add(Badge("FIRST BLOOD", "nxl-badge--firstblood"));
            body.Add(badges);

            var bsp = new VisualElement(); bsp.style.flexGrow = 1; body.Add(bsp);
            card.Add(body);

            var foot = new VisualElement(); foot.AddToClassList("nxl-hist-foot");
            foot.Add(FootT(ShortDateTime(m.endedAt)));
            var durWrap = new VisualElement(); durWrap.AddToClassList("nxl-foot-dur");
            var clk = new VisualElement(); clk.AddToClassList("nxl-foot-clock"); durWrap.Add(clk);
            var dur = new Label(m.durationSec > 0 ? FmtDur(m.durationSec) : "–"); dur.AddToClassList("nxl-hist-foot-t"); durWrap.Add(dur);
            foot.Add(durWrap); card.Add(foot);
            return card;
        }

        private static VisualElement Emblem(string tier, int size) { var e = new VisualElement(); e.AddToClassList("nxl-emblem"); NexusLobbyController.SetEmblem(e, tier); e.style.width = size; e.style.height = size; e.style.flexShrink = 0; return e; }
        private static VisualElement KdIcon(string cls) { var e = new VisualElement(); e.AddToClassList("nxl-kd-ic"); e.AddToClassList(cls); return e; }
        private static string ShortDateTime(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "—";
            string d = iso.Length >= 10 ? iso.Substring(0, 10) : iso;
            string t = iso.Length >= 16 && iso[10] == 'T' ? iso.Substring(11, 5) : "";
            return t.Length > 0 ? d + " " + t : d;
        }

        private void OpenDetail(NexusBackend.MatchRow m, DetailMode mode = DetailMode.History)
        {
            if (_histDetail == null) return;
            _detailMode = mode;
            _histDetail.Clear();
            var me = Mine(m);
            NexusBackend.MatchPlayerRow opp = null;
            if (m.players != null) foreach (var p in m.players) if (p.slot != m.slot) { opp = p; break; }
            bool r1 = m.format == "ranked1v1";
            bool flawless = Mathf.Min(m.scoreLeft, m.scoreRight) == 0 && KoFinish(m) && m.format != "legacy5v5"; // Q53 + D13
            int myScore = m.slot == 0 ? m.scoreLeft : m.scoreRight;      // skor KAMU di kiri, apa pun slot-nya
            int oppScore = m.slot == 0 ? m.scoreRight : m.scoreLeft;

            // ===== BANNER: skor kamu | MENANG/KALAH | skor lawan, lalu meta satu baris (tak overlap) =====
            var banner = new VisualElement(); banner.AddToClassList("nxl-dtx-banner");
            var brow = new VisualElement(); brow.AddToClassList("nxl-dtx-brow");
            var sL = new Label(myScore.ToString()); sL.AddToClassList("nxl-dtx-score"); sL.AddToClassList("nxl-dtx-score--you");
            var res = new Label(ResultText(m)); res.AddToClassList("nxl-dtx-res"); res.AddToClassList("nxl-dtx-res--" + ResultKey(m));
            var sR = new Label(oppScore.ToString()); sR.AddToClassList("nxl-dtx-score"); sR.AddToClassList("nxl-dtx-score--opp");
            brow.Add(sL); brow.Add(res); brow.Add(sR); banner.Add(brow);
            string endTxt = EndText(m);
            var meta = new Label(ModeLabel(m) + "   ·   " + ShortDateTime(m.endedAt) + "   ·   " + (m.durationSec > 0 ? FmtDur(m.durationSec) : "–")
                                 + (endTxt != null ? "   ·   " + endTxt : ""));
            meta.AddToClassList("nxl-dtx-meta"); banner.Add(meta);
            _histDetail.Add(banner);

            // ===== BODY: KAMU (biru) | panel tengah | LAWAN (merah) =====
            var body = new VisualElement(); body.AddToClassList("nxl-dtx-body");
            body.Add(PlayerColumn(me, true, flawless, m, r1));
            body.Add(CenterPanel(m, me, opp, r1));
            body.Add(PlayerColumn(opp, false, flawless, m, r1));
            _histDetail.Add(body);

            // ===== FOOTER: Match ID + Salin (kiri), SATU tombol (kanan) =====
            var foot = new VisualElement(); foot.AddToClassList("nxl-dtx-foot");
            var idl = new Label("Match ID: " + (m.gameId ?? "")); idl.AddToClassList("nxl-dim");
            var cp = new Button(() => { GUIUtility.systemCopyBuffer = m.gameId ?? ""; }) { text = "Salin" };
            cp.AddToClassList("nxl-btn"); cp.AddToClassList("nxl-btn--gray"); cp.style.marginLeft = 8; cp.style.height = 30; cp.style.width = 80;
            var fsp = new VisualElement(); fsp.style.flexGrow = 1;
            Button cta = (mode == DetailMode.Result)
                ? new Button(ContinueFromResult) { text = "LANJUT" }   // NX-14: kembali ke lobby
                : new Button(CloseDetail) { text = "KEMBALI" };
            cta.AddToClassList("nxl-btn"); cta.AddToClassList(mode == DetailMode.Result ? "nxl-btn--orange" : "nxl-btn--gray");
            cta.style.width = 180; cta.style.height = 46;
            if (m.mode != "practice") { foot.Add(idl); foot.Add(cp); } foot.Add(fsp); foot.Add(cta);
            _histDetail.Add(foot);

            ShowEl(_histDetail, true); _detailOpen = true;
        }

        // Kolom pemain: header KAMU/LAWAN, potret setinggi kolom (netral, desaturate bila kalah), identitas tiap baris sendiri, badge, Poin Ladder.
        private VisualElement PlayerColumn(NexusBackend.MatchPlayerRow p, bool meSide, bool matchFlawless, NexusBackend.MatchRow m, bool ranked)
        {
            var col = new VisualElement(); col.AddToClassList("nxl-dtx-col");
            var hd = new Label(meSide ? "KAMU" : "LAWAN"); hd.AddToClassList("nxl-dtx-colhead"); hd.AddToClassList(meSide ? "nxl-dtx-colhead--you" : "nxl-dtx-colhead--opp"); col.Add(hd);

            int heroId = p != null ? p.heroId : -1;
            bool thisWon = meSide ? m.won : (p != null && p.isWinner);

            var art = new VisualElement(); art.AddToClassList("nxl-dtx-portrait"); art.AddToClassList(HeroCss(heroId));
            if (!thisWon && !m.isDraw && !IsDisputed(m)) { var ds = new VisualElement(); ds.AddToClassList("nxl-dtx-portrait-desat"); art.Add(ds); } // pihak kalah = desaturate (seri/tidak dihitung: tidak)
            col.Add(art);

            var nm = new Label(p != null && !string.IsNullOrEmpty(p.displayName) ? p.displayName : (meSide ? "Kamu" : "Lawan"));
            nm.AddToClassList("nxl-dtx-name"); nm.AddToClassList(meSide ? "nxl-dt-accent-you" : "nxl-dt-accent-opp"); col.Add(nm);

            if (m.mode != "practice")
            {
                int rating = p != null ? p.ratingBefore + p.ratingDelta : 0;
                string tier = NexusRankConfig.TierForRating(rating);
                var trow = new VisualElement(); trow.AddToClassList("nxl-dtx-tier");
                trow.Add(Emblem(tier, 24)); var tn = new Label(tier); tn.AddToClassList("nxl-dtx-tiername"); trow.Add(tn); col.Add(trow);
            }

            if (p != null) { var hl = new Label(HeroName(heroId)); hl.AddToClassList("nxl-dtx-heroline"); col.Add(hl); }

            var badges = new VisualElement(); badges.AddToClassList("nxl-dtx-badges");
            if (thisWon && matchFlawless) badges.Add(Badge("FLAWLESS", "nxl-badge--flawless"));
            if (thisWon && p != null && p.maxDeficit >= 3 && m.format != "legacy5v5") badges.Add(Badge("COMEBACK", "nxl-badge--comeback")); // D13
            if (p != null && p.firstBlood) badges.Add(Badge("FIRST BLOOD", "nxl-badge--firstblood"));
            col.Add(badges);
            if (m.mode == "practice") { var pn = new Label(meSide ? "Latihan tidak memengaruhi rating" : "Latihan"); pn.AddToClassList("nxl-dim"); col.Add(pn); }

            if (ranked && p != null)
            {
                int dG = NexusRankConfig.LadderPointsGlobal(p.ratingBefore + p.ratingDelta) - NexusRankConfig.LadderPointsGlobal(p.ratingBefore);
                if (meSide) col.Add(LadderBlock(p, dG));
                else { var d = new Label((dG >= 0 ? "+" : "") + dG + " Poin"); d.AddToClassList(dG >= 0 ? "nxl-pts-up" : "nxl-pts-dn"); d.AddToClassList("nxl-dtx-oppdelta"); col.Add(d); }
            }
            return col;
        }

        // Poin Ladder (kolom kamu): bar progres tier + delta. Mode result = isi animasi 0,8 dtk + "NAIK TIER!"; history = langsung.
        private VisualElement LadderBlock(NexusBackend.MatchPlayerRow p, int dG)
        {
            var wrap = new VisualElement(); wrap.AddToClassList("nxl-dtx-ladder");
            var cap = new Label("Poin Ladder"); cap.AddToClassList("nxl-dtx-ladcap"); wrap.Add(cap);
            int before = p.ratingBefore, after = p.ratingBefore + p.ratingDelta;
            int beforePts = Mathf.Clamp(NexusRankConfig.LadderPoints(before), 0, 100);
            int afterPts = Mathf.Clamp(NexusRankConfig.LadderPoints(after), 0, 100);
            var track = new VisualElement(); track.AddToClassList("nxl-dtx-ladtrack");
            var fill = new VisualElement(); fill.AddToClassList("nxl-dtx-ladfill"); track.Add(fill); wrap.Add(track);
            var delta = new Label((dG >= 0 ? "+" : "") + dG + " Poin"); delta.AddToClassList(dG >= 0 ? "nxl-pts-up" : "nxl-pts-dn"); delta.AddToClassList("nxl-dtx-laddelta"); wrap.Add(delta);

            bool tierUp = NexusRankConfig.TierForRating(after) != NexusRankConfig.TierForRating(before) && after > before;
            if (_detailMode == DetailMode.Result)
            {
                fill.style.width = Length.Percent(beforePts);
                int target = afterPts;
                fill.schedule.Execute(() => fill.style.width = Length.Percent(target)).StartingIn(60);
            }
            else fill.style.width = Length.Percent(afterPts);
            if (tierUp) { var pop = new Label("NAIK TIER!"); pop.AddToClassList("nxl-dtx-tierup"); wrap.Add(pop); }
            return wrap;
        }

        // Panel tengah: tab + baris tug-of-war (1v1, tanpa baris cermin ganda).
        private VisualElement CenterPanel(NexusBackend.MatchRow m, NexusBackend.MatchPlayerRow me, NexusBackend.MatchPlayerRow opp, bool r1)
        {
            var panel = new VisualElement(); panel.AddToClassList("nxl-dtx-center");
            var tabs = new VisualElement(); tabs.AddToClassList("nxl-dtx-tabs");
            var t1 = new Label("RINGKASAN"); t1.AddToClassList("nxl-dtx-tab"); t1.AddToClassList("nxl-dtx-tab--active"); tabs.Add(t1);
            panel.Add(tabs);

            var content = new VisualElement(); content.AddToClassList("nxl-dtx-tabcontent");
            // Tab "JALANNYA MATCH" hanya bila koTimeline tersedia (match T2+). Match lama tanpa data: tab tidak tampil.
            var kos = ParseTimeline(m.koTimeline);
            VisualElement timeline = null;
            if (kos != null)
            {
                var t2 = new Label("JALANNYA MATCH"); t2.AddToClassList("nxl-dtx-tab"); t2.AddToClassList("nxl-dtx-tab--click"); tabs.Add(t2);
                timeline = TimelinePanel(m, kos);
                ShowEl(timeline, false);
                var tl = timeline;
                t1.AddToClassList("nxl-dtx-tab--click");
                t1.RegisterCallback<ClickEvent>(_ => { t1.AddToClassList("nxl-dtx-tab--active"); t2.RemoveFromClassList("nxl-dtx-tab--active"); ShowEl(content, true); ShowEl(tl, false); });
                t2.RegisterCallback<ClickEvent>(_ => { t2.AddToClassList("nxl-dtx-tab--active"); t1.RemoveFromClassList("nxl-dtx-tab--active"); ShowEl(content, false); ShowEl(tl, true); });
            }
            if ((r1 || m.mode == "practice") && me != null && opp != null)
            {
                content.Add(TugRow("KO", me.kills.ToString(), opp.kills.ToString(), me.kills, opp.kills, true));
                content.Add(TugInt("Damage diberikan", me.damageDealt, opp.damageDealt));
                content.Add(TugInt("Critical", me.criticals, opp.criticals));
                content.Add(TugInt("Guard crush", me.guardCrushes, opp.guardCrushes));
                content.Add(TugInt("Gear drop", me.gearDrops, opp.gearDrops));
                content.Add(TugInt("Dodge berhasil", me.dodges, opp.dodges));
                content.Add(TugInt("Serangan di-guard", me.hitsBlocked, opp.hitsBlocked));
                content.Add(TugAcc("Akurasi skill", me.skillsUsed, me.skillsHit, opp.skillsUsed, opp.skillsHit));
                content.Add(TugInt("Kill streak terpanjang", me.maxKillStreak, opp.maxKillStreak));
            }
            else
            {
                var na = new Label(IsDisputed(m) ? "Statistik tidak dicatat — hasil match ini tidak dihitung." : "Statistik detail tidak tersedia (format lama).");
                na.AddToClassList("nxl-dim"); na.style.marginTop = 16; na.style.whiteSpace = WhiteSpace.Normal; content.Add(na);
            }
            panel.Add(content);
            if (timeline != null) panel.Add(timeline);
            return panel;
        }

        // ---- T2: hasil (menang/kalah/seri), sebab akhir, jalannya match ----
        private static bool KoFinish(NexusBackend.MatchRow m) => string.IsNullOrEmpty(m.endReason) || m.endReason == "ko"; // match lama = KO
        private static bool IsDisputed(NexusBackend.MatchRow m) => m.status == "Disputed";
        private static string ResultKey(NexusBackend.MatchRow m) => IsDisputed(m) ? "disputed" : m.isDraw ? "draw" : m.won ? "win" : "loss";
        private static string ResultText(NexusBackend.MatchRow m) => IsDisputed(m) ? "TIDAK DIHITUNG" : m.isDraw ? "SERI" : m.won ? "MENANG" : "KALAH";
        // NX-21: alasan Disputed dalam bahasa pemain; rating & statistik tidak berubah.
        private static string EndText(NexusBackend.MatchRow m) =>
            IsDisputed(m) ? (m.disputeReason == "mismatch" ? "Laporan kedua pemain tidak cocok"
                           : m.disputeReason == "sanity" ? "Gagal pemeriksaan kewajaran hasil"
                           : m.disputeReason == "invariant" ? "Statistik melanggar aturan 1v1"
                           : "Hasil tidak dapat diverifikasi")
            : m.endReason == "time" ? "Waktu habis"
            : m.endReason == "forfeit" ? (m.isDraw ? "WO (dua pihak AFK)" : m.won ? "Menang WO" : "Kalah WO")
            : null;

        [System.Serializable] private sealed class KoEv { public int Tick; public int KillerSlot; }
        [System.Serializable] private sealed class KoWrap { public KoEv[] items; }
        private static KoEv[] ParseTimeline(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<KoWrap>("{\"items\":" + json + "}")?.items; } catch { return null; }
        }

        private const int SimTickRate = 60; // = NxTuning.TickRate (tick → detik)
        private VisualElement TimelinePanel(NexusBackend.MatchRow m, KoEv[] kos)
        {
            var list = new VisualElement(); list.AddToClassList("nxl-dtx-tabcontent");
            int you = 0, opp = 0;
            foreach (var k in kos)
            {
                bool mine = k.KillerSlot == m.slot;
                if (mine) you++; else opp++;
                int sec = k.Tick / SimTickRate;
                var row = new VisualElement(); row.AddToClassList("nxl-dtx-ko");
                var t = new Label($"{sec / 60}:{sec % 60:00}"); t.AddToClassList("nxl-dtx-ko-time"); row.Add(t);
                var who = new Label(mine ? "KAMU mencetak KO" : "LAWAN mencetak KO"); who.AddToClassList("nxl-dtx-ko-who");
                who.AddToClassList(mine ? "nxl-dt-accent-you" : "nxl-dt-accent-opp"); row.Add(who);
                var sc = new Label($"{you} - {opp}"); sc.AddToClassList("nxl-dtx-ko-score"); row.Add(sc);
                list.Add(row);
            }
            if (kos.Length == 0) { var none = new Label("Tidak ada KO di match ini."); none.AddToClassList("nxl-dtx-ko-end"); list.Add(none); }
            string endTxt = EndText(m);
            if (endTxt != null) { var e = new Label(endTxt + (m.isDraw ? " — seri" : "")); e.AddToClassList("nxl-dtx-ko-end"); list.Add(e); }
            return list;
        }

        // Ikon per baris statistik (kelas USS → sprite Sprites/ic_s_*.png / ic_swords.png).
        private static string StatIcon(string label) => label switch
        {
            "KO" => "nxl-stat-ic--ko",
            "Damage diberikan" => "nxl-stat-ic--damage",
            "Critical" => "nxl-stat-ic--critical",
            "Guard crush" => "nxl-stat-ic--guardcrush",
            "Gear drop" => "nxl-stat-ic--geardrop",
            "Dodge berhasil" => "nxl-stat-ic--dodge",
            "Serangan di-guard" => "nxl-stat-ic--blocked",
            "Akurasi skill" => "nxl-stat-ic--accuracy",
            "Kill streak terpanjang" => "nxl-stat-ic--streak",
            _ => null,
        };

        private static VisualElement TugInt(string label, int you, int opp) => TugRow(label, you.ToString(), opp.ToString(), you, opp, true);
        private static VisualElement TugAcc(string label, int suMe, int shMe, int suOpp, int shOpp)
        {
            bool defMe = suMe > 0, defOpp = suOpp > 0;   // skillsUsed=0 → "–", bukan 0%
            int aMe = defMe ? Mathf.RoundToInt(100f * shMe / suMe) : 0;
            int aOpp = defOpp ? Mathf.RoundToInt(100f * shOpp / suOpp) : 0;
            return TugRow(label, defMe ? aMe + "%" : "–", defOpp ? aOpp + "%" : "–", aMe, aOpp, defMe && defOpp);
        }

        // Satu baris tug-of-war: label tengah atas + bar proporsi (kiri biru = a/(a+b), kanan merah = sisa), nilai di kedua ujung (putih, sisi besar bold).
        // defined=false atau total 0 → bar netral abu, tanpa pemenang. Warna ikut SISI, bukan baik/buruk. TANPA emas.
        private static VisualElement TugRow(string label, string youStr, string oppStr, int youN, int oppN, bool defined)
        {
            var row = new VisualElement(); row.AddToClassList("nxl-dtx-trow");
            var head = new VisualElement(); head.AddToClassList("nxl-dtx-thead");
            if (StatIcon(label) is { } icCls) { var ic = new VisualElement(); ic.AddToClassList("nxl-stat-ic"); ic.AddToClassList(icCls); head.Add(ic); }
            var lab = new Label(label); lab.AddToClassList("nxl-dtx-tlabel"); head.Add(lab);
            row.Add(head);

            var line = new VisualElement(); line.AddToClassList("nxl-dtx-tbarline");
            var yv = new Label(youStr); yv.AddToClassList("nxl-dtx-tval"); yv.AddToClassList("nxl-dtx-tval--you"); if (defined && youN > oppN) yv.AddToClassList("nxl-dtx-tval--big");
            var bar = new VisualElement(); bar.AddToClassList("nxl-dtx-tbar");
            int tot = Mathf.Abs(youN) + Mathf.Abs(oppN);
            if (!defined || tot == 0) { var nz = new VisualElement(); nz.AddToClassList("nxl-dtx-tneutral"); bar.Add(nz); }
            else
            {
                float yf = (float)Mathf.Abs(youN) / tot;
                var by = new VisualElement(); by.AddToClassList("nxl-dtx-tbar-you"); by.style.width = Length.Percent(yf * 100f);
                var bo = new VisualElement(); bo.AddToClassList("nxl-dtx-tbar-opp");
                bar.Add(by); bar.Add(bo);
            }
            var ov = new Label(oppStr); ov.AddToClassList("nxl-dtx-tval"); ov.AddToClassList("nxl-dtx-tval--opp"); if (defined && oppN > youN) ov.AddToClassList("nxl-dtx-tval--big");
            line.Add(yv); line.Add(bar); line.Add(ov); row.Add(line);
            return row;
        }

        private static string HeroCss(int h) => h == 0 ? "nxl-hero-titan" : h == 1 ? "nxl-hero-pulse" : "nxl-hero-unknown";
        private static string HeroName(int h) => h == 0 ? "TITAN-01" : h == 1 ? "PULSE-X" : "—";
        private static string ModeLabel(NexusBackend.MatchRow m) { var md = ModeOf(m); return md == "ranked" ? "RANKED" : md == "battle" ? "BATTLE" : "LATIHAN"; }
        // Chip prestasi = ikon + teks (NX-12 sisa: ikon per-badge). Ikon dipilih lewat kelas modifier di USS.
        private static VisualElement Badge(string text, string cls)
        {
            var chip = new VisualElement(); chip.AddToClassList("nxl-badge"); chip.AddToClassList("nxl-badge--chip"); chip.AddToClassList(cls);
            var ic = new VisualElement(); ic.AddToClassList("nxl-badge-ic"); chip.Add(ic);
            var l = new Label(text); l.AddToClassList("nxl-badge-txt"); chip.Add(l);
            return chip;
        }
        private static Label FootT(string t) { var l = new Label(t); l.AddToClassList("nxl-hist-foot-t"); return l; }

        // ---- Statistik (Tab 1.3) ----
        private void SetStatsFilter(int f)
        {
            _statsFilter = f;
            ActiveTab("sf-all", f == 0); ActiveTab("sf-ranked", f == 1); ActiveTab("sf-battle", f == 2);
            RefreshStats();
        }

        private void EnsureStats() { RefreshStats(); if (!_statsFetched) FetchStats(); }

        private async void FetchStats()
        {
            var b = game != null ? game.Backend : null; if (b == null) return;
            _statMatches = await b.GetMatchHistoryAsync();
            _statsFetched = true;
            RefreshStats();
        }

        // Mode seragam, case-insensitive (docs/32): "ranked"/"battle".
        private static string ModeOf(NexusBackend.MatchRow m) => (m.mode ?? "").Trim().ToLowerInvariant();

        private void RefreshStats()
        {
            var b = game != null ? game.Backend : null; if (b == null) return;
            var all = _statMatches ?? System.Array.Empty<NexusBackend.MatchRow>();
            var f = new List<NexusBackend.MatchRow>();
            foreach (var m in all)
            {
                var mode = ModeOf(m);
                if (_statsFilter == 0 || (_statsFilter == 1 && mode == "ranked") || (_statsFilter == 2 && mode == "battle")) f.Add(m);
            }

            int matches = f.Count;
            bool empty = matches == 0;
            SetText("stats-msg", _statsFilter == 0 ? "Belum ada pertandingan. Statistik akan muncul setelah kamu bermain." : "Belum ada pertandingan di mode ini.");
            ShowEl(_statsEmpty, empty); ShowEl(_statsContent, !empty);
            if (empty) return;

            // SATU SUMBER (docs/32): semua angka dihitung dari data per-match (f), bukan agregat /me.
            int wins = 0, losses = 0, kills = 0, deaths = 0, assists = 0;
            int kdaMatches = 0, maxKill = 0, durSum = 0, durCount = 0;
            bool anyKda = false, anyMissing = false;
            foreach (var m in f)
            {
                if (m.isDraw) { } else if (m.won) wins++; else losses++;   // seri tak dihitung menang/kalah (T2)
                var mp = Mine(m);
                // K/D HANYA dari format 1v1 valid (docs/32). Match lama 5v5-bot → K/D tak dihitung ("–").
                bool kdValid = m.format == "ranked1v1";
                if (mp != null && kdValid) { kills += mp.kills; deaths += mp.deaths; assists += mp.assists; kdaMatches++; anyKda = true; if (mp.kills > maxKill) maxKill = mp.kills; }
                else anyMissing = true;
                if (m.durationSec > 0) { durSum += m.durationSec; durCount++; } else anyMissing = true;
                if (m.heroId <= 0) anyMissing = true;
            }
            double winrate = wins + losses > 0 ? System.Math.Round(100.0 * wins / (wins + losses), 1) : 0;
            double kda = deaths > 0 ? System.Math.Round((kills + assists) / (double)deaths, 2) : (kills + assists);

            SetText("st-matches", matches.ToString());
            SetText("st-winrate", winrate.ToString("0.#") + "%");
            SetText("st-kda", anyKda ? kda.ToString("0.00") : "–");
            SetText("st-wins", wins.ToString()); SetText("st-losses", losses.ToString());
            // Kill/Death/Assist + Kill Tertinggi dari SATU sumber → tidak saling bertentangan.
            SetText("st-kills", anyKda ? kills.ToString() : "–");
            SetText("st-deaths", anyKda ? deaths.ToString() : "–");
            SetText("st-assists", anyKda ? assists.ToString() : "–");
            SetText("st-maxkill", anyKda ? maxKill.ToString() : "–");

            int streak = 0, cur = 0;
            for (int i = f.Count - 1; i >= 0; i--) { if (f[i].won) { cur++; if (cur > streak) streak = cur; } else cur = 0; }
            SetText("st-streak", streak.ToString());
            SetText("st-avgdur", durCount > 0 ? FmtDur(durSum / durCount) : "–");

            ShowEl(_root.Q<Label>("stats-note"), anyMissing);

            BuildWrSeries(f);
            BuildRankSeries(all);
        }

        private void BuildWrSeries(List<NexusBackend.MatchRow> f)
        {
            int n = f.Count;
            if (n < 2) { _wrSeries = null; ShowEl(_wrCanvas, false); SetGraphEmpty("st-wr-empty", true, "Grafik muncul setelah 2 pertandingan."); FillDates("st-wr-dates", null); }
            else
            {
                var s = new float[n]; int w = 0;
                for (int i = 0; i < n; i++) { if (f[n - 1 - i].won) w++; s[i] = 100f * w / (i + 1); }
                _wrSeries = s; ShowEl(_wrCanvas, true); SetGraphEmpty("st-wr-empty", false, "");
                FillDates("st-wr-dates", f); // f newest-first → kiri(lama)…kanan(baru)
            }
            if (_wrCanvas != null) _wrCanvas.MarkDirtyRepaint();
        }

        // X-axis tanggal: kiri = match terlama, kanan = terbaru (searah grafik i=0→n-1 kronologis).
        private void FillDates(string container, List<NexusBackend.MatchRow> rows)
        {
            var c = _root.Q(container); if (c == null) return;
            c.Clear();
            if (rows == null || rows.Count < 2) return;
            int n = rows.Count;
            c.Add(DateLbl(ShortDate(rows[n - 1].endedAt)));
            if (n > 2) c.Add(DateLbl(ShortDate(rows[(n - 1) / 2].endedAt)));
            c.Add(DateLbl(ShortDate(rows[0].endedAt)));
        }
        private static Label DateLbl(string t) { var l = new Label(t); l.AddToClassList("nxl-dim"); l.style.fontSize = 11; return l; }
        private static string ShortDate(string iso) => string.IsNullOrEmpty(iso) ? "—" : (iso.Length >= 10 ? iso.Substring(0, 10) : iso);

        private void BuildRankSeries(NexusBackend.MatchRow[] all)
        {
            var b = game.Backend;
            var r = new List<NexusBackend.MatchRow>();
            foreach (var m in all) if (ModeOf(m) == "ranked") r.Add(m);
            int n = r.Count;
            if (n < 2 || b == null) { _rankSeries = null; ShowEl(_rankCanvas, false); SetGraphEmpty("st-rank-empty", true, "Grafik muncul setelah 2 match ranked."); FillDates("st-rank-dates", null); }
            else
            {
                var nf = new float[n]; float sdl = 0f;
                for (int k = 0; k < n; k++) { nf[k] = b.Rating - sdl; sdl += r[k].ratingDelta; }
                var s = new float[n]; for (int j = 0; j < n; j++) s[j] = nf[n - 1 - j];
                _rankSeries = s; ShowEl(_rankCanvas, true); SetGraphEmpty("st-rank-empty", false, "");
                FillDates("st-rank-dates", r);
            }
            if (_rankCanvas != null) _rankCanvas.MarkDirtyRepaint();
        }

        private void SetGraphEmpty(string name, bool show, string text) { var l = _root.Q<Label>(name); if (l != null) { l.text = text; ShowEl(l, show); } }

        private void OnDrawWr(MeshGenerationContext ctx) => DrawSeries(ctx, _wrSeries, 0f, 100f, false);
        private void OnDrawRank(MeshGenerationContext ctx) => DrawSeries(ctx, _rankSeries, 1200f, 2000f, true);

        private static void DrawSeries(MeshGenerationContext ctx, float[] s, float lo, float hi, bool tierBands)
        {
            if (s == null || s.Length < 2) return;
            var rect = ctx.visualElement.contentRect; float W = rect.width, H = rect.height;
            if (W < 5f || H < 5f) return;
            float padL = 10f, padR = 10f, padT = 8f, padB = 8f;
            float gw = W - padL - padR, gh = H - padT - padB;
            if (gw < 5f || gh < 5f) return;
            var p = ctx.painter2D;
            if (tierBands)
            {
                var bands = NexusRankConfig.Bands;
                for (int i = 0; i < bands.Length; i++)
                {
                    var bn = bands[i]; if (bn.Max <= lo || bn.Min >= hi) continue;
                    float mn = Mathf.Max(bn.Min, lo), mx = Mathf.Min(bn.Max, hi);
                    float y0 = padT + gh * (1f - (mx - lo) / (hi - lo)), y1 = padT + gh * (1f - (mn - lo) / (hi - lo));
                    // band tier = emas redup (-dim), bukan cyan
                    p.fillColor = (i % 2 == 0) ? new Color(0.961f, 0.773f, 0.259f, 0.045f) : new Color(0.961f, 0.773f, 0.259f, 0.08f);
                    p.BeginPath(); p.MoveTo(new Vector2(padL, y0)); p.LineTo(new Vector2(padL + gw, y0));
                    p.LineTo(new Vector2(padL + gw, y1)); p.LineTo(new Vector2(padL, y1)); p.ClosePath(); p.Fill();
                }
            }
            int n = s.Length;
            p.strokeColor = new Color(0.251f, 0.588f, 1f, 1f); p.lineWidth = 2.5f; p.BeginPath(); // garis data = biru (ally)
            for (int i = 0; i < n; i++)
            {
                float x = padL + gw * (float)i / (n - 1);
                float y = padT + gh * (1f - (Mathf.Clamp(s[i], lo, hi) - lo) / (hi - lo));
                if (i == 0) p.MoveTo(new Vector2(x, y)); else p.LineTo(new Vector2(x, y));
            }
            p.Stroke();
            for (int i = 0; i < n; i++)
            {
                float x = padL + gw * (float)i / (n - 1);
                float y = padT + gh * (1f - (Mathf.Clamp(s[i], lo, hi) - lo) / (hi - lo));
                // titik terakhir = fokus → cyan; lainnya = biru (ally)
                p.fillColor = (i == n - 1) ? new Color(0.31f, 0.82f, 1f, 1f) : new Color(0.251f, 0.588f, 1f, 1f);
                p.BeginPath(); p.Arc(new Vector2(x, y), i == n - 1 ? 5f : 3f, Angle.Degrees(0f), Angle.Degrees(360f)); p.Fill();
            }
        }

        private static NexusBackend.MatchPlayerRow Mine(NexusBackend.MatchRow m)
        {
            if (m.players == null) return null;
            foreach (var pr in m.players) if (pr.slot == m.slot) return pr;
            return null;
        }
        private static string FmtDur(int sec) => (sec / 60) + ":" + (sec % 60).ToString("00");
        private void ClickEl(string n, System.Action cb) { var e = _root.Q(n); if (e != null) e.RegisterCallback<ClickEvent>(_ => cb()); }
        private void ActiveTab(string n, bool v) { var e = _root.Q(n); if (e != null) e.EnableInClassList("nxl-tab--active", v); }

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
