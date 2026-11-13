using System;
using System.Threading.Tasks;
using Photon.Deterministic;
using Photon.Realtime;
using Quantum;
using UnityEngine;

namespace Nexus
{
    /// <summary>
    /// SPIKE networking: connect ke Photon Cloud (TANPA backend/auth), quick match 2 pemain,
    /// jalankan sesi Quantum Nexus. Adaptasi minimal dari PongGame. Tekan "Quick Match" di 2 instance.
    /// </summary>
    public class NexusGame : MonoBehaviour
    {
        private enum Page { Auth, Menu, Profile, Connecting, InMatch }

        [Tooltip("RuntimeConfig (Map + Nexus SystemsConfig) — di-wire oleh NexusSetup.CreateOnlineScene.")]
        public RuntimeConfig RuntimeConfig = new RuntimeConfig();

        private RealtimeClient _client;
        private Page _screen = Page.Auth;
        private string _status = "";
        private bool _lag;

        // --- Lobby UITK baru (berdampingan dgn IMGUI lama) ---
        [Tooltip("ON: lobby UI Toolkit (NexusLobbyController) tampil, menu IMGUI lama disembunyikan. OFF: lobby IMGUI lama.")]
        public bool UseNewLobby = false;
        /// <summary>Pesan error koneksi/match terakhir (untuk banner disconnect di lobby baru). Kosong = tak ada.</summary>
        public string LastError { get; private set; } = "";
        public void ClearError() => LastError = "";
        public NexusBackend Backend => _backend;
        public bool IsAuth => _screen == Page.Auth;
        public bool IsMenu => _screen == Page.Menu;
        public bool IsConnecting => _screen == Page.Connecting;
        public bool IsInMatch => _screen == Page.InMatch;
        public string StatusText => _status;
        /// <summary>True saat lawan ranked sudah ketemu (fase layar VS ~1.8s sebelum masuk match).</summary>
        public bool OpponentFound { get; private set; }
        public void UiRanked() => RankedMatch();
        public void UiQuickMatch() => QuickMatch();
        public void UiCreateRoom() => CreateRoom();
        public void UiOpenProfile() => OpenProfile();
        public void UiLogout() => Logout();
        public void UiCancel() { _screen = Page.Menu; OpponentFound = false; } // ranked loop lihat screen != Connecting → LeaveQueue + batal
        // Auth untuk lobby UITK (pakai field + DoAuth/DoGuest yang sama dgn IMGUI lama).
        public string AuthMsg => _authMsg;
        public bool AuthBusy => _authBusy;
        public void UiLogin(string u, string p) { _fUser = u; _fPass = p; _authRegister = false; DoAuth(); }
        public void UiRegister(string u, string p, string name) { _fUser = u; _fPass = p; _fName = name; _authRegister = true; DoAuth(); }
        public void UiGuest() => DoGuest();
        // Profil UITK
        [Tooltip("ON: halaman Profil UI Toolkit (ProfileController); profil IMGUI lama disembunyikan.")]
        public bool UseNewProfile = false;
        public bool IsProfile => _screen == Page.Profile;
        public void UiProfileBack() { _screen = Page.Menu; }

        // --- Faksi: onboarding UITK sebelum lobby (docs/35). Guest ikut memilih; terkunci satu season. ---
        public int Faction => _backend?.Faction ?? 0;
        public bool NeedsFaction => IsMenu && _backend != null && _backend.LoggedIn && _backend.Faction == 0;
        public bool FactionBusy { get; private set; }
        public string FactionMsg { get; private set; } = "";
        public async void UiSetFaction(int f)
        {
            if (FactionBusy || _backend == null || (f != 1 && f != 2)) return;
            FactionBusy = true; FactionMsg = "";
            bool ok = await _backend.SetFactionAsync(f);
            FactionBusy = false;
            // sukses → _backend.Faction != 0 → NeedsFaction=false → NexusLobbyController tampilkan lobby.
            if (!ok) FactionMsg = "Gagal mengunci faksi (terkunci / backend mati?). Coba lagi.";
        }

        // --- Auth (login/register) ---
        private string _fUser = "", _fPass = "", _fName = "";
        private bool _authRegister, _authBusy;
        private string _authMsg = "";

        // --- Ranked (meta backend) ---
        [Tooltip("URL PhotonBackend.Api. Jalankan: dotnet run --project ...PhotonBackend.Api --urls http://localhost:5080")]
        public string BackendUrl = "http://localhost:5080";
        private NexusBackend _backend;
        private string _gameId = "";
        private int _mySlot = -1;
        private bool _reported;
        private string _resultText = "";
        private NexusBackend.LeaderRow[] _board;
        private bool _boardOpen;
        private NexusBackend.MatchRow[] _history;
        private string _matchMode = "";   // "ranked" | "battle" — dikirim saat report
        private float _matchStart;          // realtime saat match mulai → durasi

        private void ToggleLag()
        {
            _lag = !_lag;
            if (_client == null) return;
            var peer = _client.RealtimePeer;
            peer.IsSimulationEnabled = _lag;
            // ~150 ms RTT (75 tiap arah). Uji Go/No-Go spike.
            peer.NetworkSimulationSettings.IncomingLag = 75;
            peer.NetworkSimulationSettings.OutgoingLag = 75;
        }

        public static Frame PredictedFrame()
        {
            var runner = QuantumRunner.Default;
            if (runner == null || runner.Game == null) return null;
            return runner.Game.Frames.Predicted;
        }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            // WAJIB: tetap jalan saat window tak fokus. Tanpa ini, instance ke-2 di 1 PC di-throttle →
            // Photon tak di-service → "ClientServiceInactivity" disconnect → pemain drop dari sesi → bot & Disputed.
            Application.runInBackground = true;
        }

        private async void QuickMatch() => await ConnectSafe(null);
        private async void CreateRoom() => await ConnectSafe("nx-" + UnityEngine.Random.Range(1000, 9999));

        /// <summary>Flow RANKED: login tamu → antre MMR → room Photon dari matchmaking → main → lapor hasil.</summary>
        private async void RankedMatch()
        {
            if (_backend == null || !_backend.LoggedIn) { _screen = Page.Auth; return; } // harus login dulu
            _screen = Page.Connecting; LastError = ""; OpponentFound = false; _matchMode = "ranked";
            try
            {
                await _backend.LeaveQueueAsync(); // retry bersih: hapus assignment/antrean lama dulu
                _status = "Mencari lawan (MMR)...";
                var mm = await _backend.JoinQueueAsync("asia", Application.version);
                if (mm != null && mm.state == "VersionMismatch")
                {
                    _status = string.IsNullOrEmpty(mm.message) ? "Versi game berbeda, perbarui game." : mm.message;
                    LastError = _status;
                    _screen = Page.Menu;
                    return;
                }
                for (int i = 0; i < 90 && (mm == null || mm.state != "Matched"); i++)
                {
                    await Task.Delay(1000);
                    if (_screen != Page.Connecting) { await _backend.LeaveQueueAsync(); return; } // user tekan Cancel
                    mm = await _backend.QueueStatusAsync();
                    // Pencarian lama → beri tahu rentang sedang diperluas (biar tidak terasa menggantung).
                    _status = i < 30 ? $"Mencari lawan (MMR)... {i + 1}" : $"Memperluas pencarian... {i + 1}";
                }
                if (mm == null || mm.state != "Matched" || string.IsNullOrEmpty(mm.gameId))
                {
                    _status = "Belum ada lawan. Buka klien ke-2 lalu klik Ranked.";
                    await _backend.LeaveQueueAsync();
                    _screen = Page.Menu;
                    return;
                }

                _gameId = mm.gameId;
                _mySlot = -1; _reported = false; _resultText = "";
                OpponentFound = true;                  // picu layar VS di lobby UITK
                _status = "Lawan ditemukan!";
                await Task.Delay(1800);                 // tampilkan VS ~1.8 detik
                if (_screen != Page.Connecting) { OpponentFound = false; await _backend.LeaveQueueAsync(); return; } // dibatalkan saat VS
                await StartSessionAsync(_gameId); // StartSessionAsync men-set _screen = InMatch
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                await LeaveAsync();
                _status = "Ranked gagal: " + e.Message;
                LastError = "Ranked gagal: " + e.Message;
                _screen = Page.Menu;
            }
        }

        private async void OpenLeaderboard()
        {
            _backend ??= new NexusBackend(BackendUrl);
            _board = null;        // tampil "memuat..."
            _boardOpen = true;
            _board = await _backend.GetLeaderboardAsync(20);
        }

        private void DrawAuth(float w)
        {
            const float ww = 320, hh = 320;
            GUILayout.BeginArea(new Rect(w / 2 - ww / 2, 36, ww, hh), GUI.skin.box);
            GUILayout.Label("NEXUS SAGA", new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter });
            GUILayout.Label(_authRegister ? "Daftar Akun Baru" : "Login", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(6);
            GUILayout.Label("Username");
            _fUser = GUILayout.TextField(_fUser, 24);
            GUILayout.Label("Password");
            _fPass = GUILayout.PasswordField(_fPass, '*', 32);
            if (_authRegister)
            {
                GUILayout.Label("Nama Tampilan");
                _fName = GUILayout.TextField(_fName, 24);
            }
            GUILayout.Space(6);
            GUI.enabled = !_authBusy;
            if (GUILayout.Button(_authRegister ? "Daftar" : "Login", GUILayout.Height(34))) DoAuth();
            if (GUILayout.Button(_authRegister ? "Sudah punya akun? Login" : "Belum punya akun? Daftar"))
            { _authRegister = !_authRegister; _authMsg = ""; }
            if (GUILayout.Button("Main sebagai Guest")) DoGuest();
            GUI.enabled = true;
            if (_authMsg.Length > 0) GUILayout.Label(_authMsg);
            GUILayout.EndArea();
        }

        private async void DoAuth()
        {
            _backend ??= new NexusBackend(BackendUrl);
            var u = _fUser.Trim();
            var p = _fPass;
            if (u.Length < 3 || p.Length < 6) { _authMsg = "Username min 3 huruf, password min 6."; return; }
            _authBusy = true; _authMsg = _authRegister ? "Mendaftar..." : "Masuk...";
            bool ok = _authRegister
                ? await _backend.RegisterAsync(u, p, _fName.Trim().Length > 0 ? _fName.Trim() : u)
                : await _backend.LoginAsync(u, p);
            _authBusy = false;
            if (ok) { _fPass = ""; _authMsg = ""; _screen = Page.Menu; }
            else _authMsg = _authRegister ? "Gagal daftar — username sudah dipakai atau server tak terhubung." : "Username atau password salah, atau server tak terhubung.";
        }

        private async void DoGuest()
        {
            _backend ??= new NexusBackend(BackendUrl);
            _authBusy = true; _authMsg = "Masuk sebagai guest...";
            bool ok = await _backend.GuestLoginAsync(DeviceId(), "Guest" + UnityEngine.Random.Range(100, 999));
            _authBusy = false;
            if (ok) { _authMsg = ""; _screen = Page.Menu; }
            else _authMsg = "Server tidak dapat dihubungi (" + BackendUrl + ").";
        }

        private void Logout()
        {
            _backend = null;
            _fUser = ""; _fPass = ""; _fName = ""; _authMsg = "";
            _screen = Page.Auth;
        }

        private async void OpenProfile()
        {
            if (_backend == null || !_backend.LoggedIn) return;
            _screen = Page.Profile;
            _history = null; // "memuat..."
            await _backend.RefreshMeAsync();
            _history = await _backend.GetMatchHistoryAsync();
        }

        private void DrawProfile(float w)
        {
            var b = _backend;
            const float pw = 480, ph = 560;
            GUILayout.BeginArea(new Rect(w / 2 - pw / 2, 16, pw, ph), GUI.skin.box);
            var h1 = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            var h2 = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            GUILayout.Label("PROFIL PEMAIN", h1);

            if (b == null || !b.LoggedIn)
            { GUILayout.Label("Belum login."); if (GUILayout.Button("Kembali")) _screen = Page.Menu; GUILayout.EndArea(); return; }

            var who = string.IsNullOrEmpty(b.Username) ? b.DisplayName + " (Guest)" : b.Username;
            string fac = b.Faction == 1 ? "ORDE" : b.Faction == 2 ? "LEGION" : "-";
            GUILayout.Label($"{who}       Faction: {fac}");
            GUILayout.Label($"Account Rank: {b.AccountRank}  (Lv {b.AccountLevel}, XP {b.AccountXp})");
            GUILayout.Space(6);

            GUILayout.Label("SOLO 1v1 LADDER", h2);
            GUILayout.Label($"Tier: {b.RankTier}        Rating (MMR): {b.Rating}");
            GUILayout.Space(6);

            GUILayout.Label("STATISTIK", h2);
            GUILayout.Label($"Main: {b.MatchesPlayed}    Menang: {b.Wins}    Kalah: {b.Losses}    Win%: {b.WinRate:0.#}");
            GUILayout.Label($"Kills: {b.Kills}    Deaths: {b.Deaths}    Assists: {b.Assists}    KDA: {b.Kda:0.00}");
            GUILayout.Space(6);

            if (b.Faction == 0 && !string.IsNullOrEmpty(b.Username))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Pilih Faction:", GUILayout.Width(100));
                if (GUILayout.Button("ORDE")) _ = b.SetFactionAsync(1);
                if (GUILayout.Button("LEGION")) _ = b.SetFactionAsync(2);
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
            }

            GUILayout.Label("RIWAYAT TERAKHIR", h2);
            if (_history == null) GUILayout.Label("Memuat...");
            else if (_history.Length == 0) GUILayout.Label("(belum ada match selesai)");
            else
                for (int i = 0; i < _history.Length && i < 8; i++)
                {
                    var m = _history[i];
                    string res = m.won ? "MENANG" : "KALAH";
                    string d = m.ratingDelta >= 0 ? "+" + m.ratingDelta : m.ratingDelta.ToString();
                    GUILayout.Label($"{res}    {m.scoreLeft}-{m.scoreRight}    (rating {d})");
                }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Kembali ke Menu", GUILayout.Height(32))) _screen = Page.Menu;
            GUILayout.EndArea();
        }

        private unsafe void Update()
        {
            if (_screen != Page.InMatch || _backend == null) return;
            var runner = QuantumRunner.Default;
            if (runner == null || runner.Game == null) return;

            if (_mySlot < 0)
            {
                var locals = runner.Game.GetLocalPlayers();
                if (locals.Count > 0) _mySlot = ((int)locals[0]) & 1; // tim in-game (0=BIRU,1=ORANYE)
            }

            if (_reported || _mySlot < 0) return;
            var frame = runner.Game.Frames.Predicted;
            if (frame == null || frame.Global->State != 2) return; // 2 = match selesai

            _reported = true;
            var locals2 = runner.Game.GetLocalPlayers();
            PlayerRef me = locals2.Count > 0 ? locals2[0] : default;

            // Hasil dasar DULU (langsung dari frame) — ini yang WAJIB tercatat di history.
            int myKills = 0, myDeaths = 0, myHero = 0;
            foreach (var (e, c) in frame.Unsafe.GetComponentBlockIterator<NxCombat>())
                if (c->Player == me) { myKills = c->Kills; myDeaths = c->Kos; myHero = c->H1Ticks > c->H0Ticks ? 1 : 0; break; }

            // Statistik detail OPSIONAL — kalau gagal dibangun, JANGAN batalkan laporan (match tetap masuk history).
            NexusBackend.PlayerStatRow[] payload = null;
            try { payload = BuildStatsPayload(frame); }
            catch (Exception ex) { Debug.LogWarning("Payload statistik gagal, lapor hasil dasar saja: " + ex.Message); }

            // T2: sebab akhir + pemenang (hasil tie-break sim) + koTimeline — deterministik, identik di kedua klien.
            byte reason = frame.Global->EndReason;
            string endReason = reason == NxMatch.ReasonTime ? "time" : reason == NxMatch.ReasonForfeit ? "forfeit" : "ko";
            int winnerSlot = frame.Global->WinnerTeam == NxMatch.Draw ? -1 : frame.Global->WinnerTeam;
            var timeline = new NexusBackend.KoEventRow[frame.Global->KoCount];
            for (int i = 0; i < timeline.Length; i++)
            {
                var ev = frame.Global->KoLog.GetPointer(i);
                timeline[i] = new NexusBackend.KoEventRow { tick = ev->Tick, killerSlot = ev->KillerSlot };
            }

            // Durasi dari tick simulasi (deterministik, sama di kedua klien), bukan jam dinding.
            int durSec = (frame.Number - frame.Global->StartTick) / frame.UpdateRate;

            _ = ReportAsync(frame.Global->ScoreA, frame.Global->ScoreB, myKills, myDeaths, myHero, payload, endReason, winnerSlot, timeline, durSec);
        }

        // Kumpulkan statistik akumulator SEMUA pemain manusia dari frame deterministik.
        // Dua klien menghasilkan array identik → backend bandingkan (docs/32 KEAMANAN MINIMUM).
        private static unsafe NexusBackend.PlayerStatRow[] BuildStatsPayload(Frame frame)
        {
            var list = new System.Collections.Generic.List<NexusBackend.PlayerStatRow>();
            foreach (var (e, c) in frame.Unsafe.GetComponentBlockIterator<NxCombat>())
            {
                if (c->Player == PlayerRef.None) continue; // hanya manusia
                var heroes = new System.Collections.Generic.List<NexusBackend.HeroStatRow>();
                if (c->H0Ticks > 0) heroes.Add(new NexusBackend.HeroStatRow { heroId = 0, playTicks = c->H0Ticks, kills = c->H0Kills, damage = FPMath.RoundToInt(c->H0Dmg) });
                if (c->H1Ticks > 0) heroes.Add(new NexusBackend.HeroStatRow { heroId = 1, playTicks = c->H1Ticks, kills = c->H1Kills, damage = FPMath.RoundToInt(c->H1Dmg) });
                list.Add(new NexusBackend.PlayerStatRow
                {
                    slot = ((int)c->Player) & 1,
                    kills = c->Kills, botKills = c->BotKills, deaths = c->Kos, heroId = c->H1Ticks > c->H0Ticks ? 1 : 0,
                    damageDealt = FPMath.RoundToInt(c->DamageDealt), damageTaken = FPMath.RoundToInt(c->DamageTaken),
                    skillsUsed = c->SkillsUsed, skillsHit = c->SkillsHit,
                    hitsBlocked = c->HitsBlocked, dodges = c->Dodges, guardCrushes = c->GuardCrushes,
                    criticals = c->Criticals, gearDrops = c->GearDrops,
                    firstBlood = c->FirstBlood != 0, maxKillStreak = c->MaxStreak, maxDeficit = c->MaxDeficit,
                    heroSwitches = c->HeroSwitches, heroes = heroes.ToArray(),
                });
            }
            list.Sort((a, b) => a.slot.CompareTo(b.slot)); // identik antar-klien
            return list.ToArray();
        }

        // Device id UNIK per-launch: editor, tiap .exe, bahkan 2 .exe di 1 mesin = akun BERBEDA → matchmaking
        // selalu bisa pasang 2 instance (tak bisa match diri sendiri). Catatan: rating reset tiap launch
        // (akun baru) — cukup untuk menguji loop ranked. Nanti diganti login akun stabil.
        private static readonly string _deviceId = "nx-" + System.Guid.NewGuid().ToString("N");
        private static string DeviceId() => _deviceId;

        private async Task ReportAsync(int scoreLeft, int scoreRight, int kills, int deaths, int heroId, NexusBackend.PlayerStatRow[] players,
                                       string endReason, int winnerSlot, NexusBackend.KoEventRow[] koTimeline, int durSec)
        {
            // T2: skor dilaporkan APA ADANYA (tak lagi dinormalkan ke 5) — waktu habis/forfeit bisa berakhir 2-1, 0-0, dst.
            int dur = Mathf.Max(0, durSec);
            var status = await _backend.ReportResultAsync(_gameId, _mySlot, scoreLeft, scoreRight, kills, deaths, 0, heroId, _matchMode, dur, 1, players,
                                                          endReason, winnerSlot, koTimeline);
            await _backend.RefreshMeAsync();
            await _backend.LeaveQueueAsync(); // bersihkan assignment agar bisa rematch
            string why = endReason == "time" ? "WAKTU HABIS — " : endReason == "forfeit" ? "MENANG WO (AFK) — " : "";
            string head = winnerSlot < 0 ? $"{why}SERI {scoreLeft}-{scoreRight}"
                        : $"{why}{(winnerSlot == 0 ? "TIM BIRU" : "TIM ORANYE")} MENANG {scoreLeft}-{scoreRight}";
            _resultText = $"{head}\nKamu: {kills} Kill / {deaths} Death\nRating: {_backend.Rating} ({_backend.RankTier})";
        }

        private async Task ConnectSafe(string roomName)
        {
            _screen = Page.Connecting; LastError = ""; _matchMode = "battle";
            _status = roomName == null ? "Mencari lawan..." : "Membuat room " + roomName;
            try { await StartSessionAsync(roomName); }
            catch (Exception e)
            {
                Debug.LogException(e);
                await LeaveAsync();
                _status = "Gagal: " + e.Message;
                LastError = "Koneksi gagal: " + e.Message;
                _screen = Page.Menu;
            }
        }

        private async Task StartSessionAsync(string roomName)
        {
            NexusInput.PendingPick = 0; // layar Pilih Hero fresh tiap match baru
            // Jumlah petarung = parameter sesi Quantum (tersinkron pasti). RANKED 2 → 1v1 tanpa bot; BATTLE 10 → 5v5 isi bot.
            int playerCount = _matchMode == "ranked" ? 2 : 10;
            var connectionArguments = new MatchmakingArguments
            {
                PhotonSettings = new AppSettings(PhotonServerSettings.Global.AppSettings),
                PluginName = "QuantumPlugin",
                RoomName = roomName,
                MaxPlayers = 2,                       // spike: 2 pemain per room
                CanOnlyJoin = false,
            };
            _client = await MatchmakingExtensions.ConnectToRoomAsync(connectionArguments);

            var sessionRunnerArguments = new SessionRunner.Arguments
            {
                RunnerFactory = QuantumRunnerUnityFactory.DefaultFactory,
                GameParameters = QuantumRunnerUnityFactory.CreateGameParameters,
                ClientId = _client.UserId,
                RuntimeConfig = RuntimeConfig ?? new RuntimeConfig(),
                SessionConfig = QuantumDeterministicSessionConfigAsset.DefaultConfig,
                GameMode = DeterministicGameMode.Multiplayer,
                PlayerCount = playerCount, // RANKED 2 (1v1) / BATTLE 10 (5v5 isi bot)
                StartGameTimeoutInSeconds = 10,
                Communicator = new QuantumNetworkCommunicator(_client),
            };

            var runner = (QuantumRunner)await SessionRunner.StartAsync(sessionRunnerArguments);
            runner.Game.AddPlayer(new RuntimePlayer { PlayerNickname = "Runner" + UnityEngine.Random.Range(10, 99) });

            _status = "";
            _matchStart = Time.realtimeSinceStartup;
            _screen = Page.InMatch;
        }

        private async Task LeaveAsync()
        {
            try { if (_backend != null) await _backend.LeaveQueueAsync(); } catch (Exception e) { Debug.LogWarning(e.Message); }
            try { await QuantumRunner.ShutdownAllAsync(); } catch (Exception e) { Debug.LogWarning(e.Message); }
            try { if (_client != null) await _client.DisconnectAsync(); } catch (Exception e) { Debug.LogWarning(e.Message); }
            _client = null;
            _mySlot = -1; _reported = false; _resultText = ""; _gameId = ""; OpponentFound = false;
        }

        private void OnGUI()
        {
            float scale = Mathf.Max(1f, UnityEngine.Screen.height / 540f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float w = UnityEngine.Screen.width / scale;

            if (_boardOpen)
            {
                const float bw = 460, bh = 400;
                GUILayout.BeginArea(new Rect(w / 2 - bw / 2, 20, bw, bh), GUI.skin.box);
                var bt = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                GUILayout.Label("LEADERBOARD — Ranked (Poin Ladder)", bt);
                GUILayout.Space(6);
                if (_board == null) GUILayout.Label("Memuat...");
                else if (_board.Length == 0) GUILayout.Label("(belum ada yang selesai placement)");
                else
                    for (int i = 0; i < _board.Length; i++)
                    {
                        var r = _board[i];
                        GUILayout.Label($"#{r.rank,2}  {r.displayName}   {r.tier} · {r.ladderPoints}   (W{r.wins}/L{r.losses} · {r.winRate:0.#}%)");
                    }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Tutup", GUILayout.Height(30))) _boardOpen = false;
                GUILayout.EndArea();
                return;
            }

            if (_screen == Page.Auth) { if (!UseNewLobby) DrawAuth(w); return; } // UITK auth saat UseNewLobby
            if (_screen == Page.Profile) { if (!UseNewProfile) DrawProfile(w); return; } // UITK profil saat UseNewProfile

            if (_screen == Page.InMatch)
            {
                if (GUI.Button(new Rect(w - 100, 10, 90, 34), "Leave")) { _ = LeaveAsync(); _screen = Page.Menu; }
                if (GUI.Button(new Rect(w - 260, 10, 150, 34), _lag ? "Lag 150ms: ON" : "Lag 150ms: OFF")) ToggleLag();
                if (_resultText.Length > 0)
                {
                    GUI.Box(new Rect(w / 2 - 190, 150, 380, 130), "HASIL RANKED");
                    GUI.Label(new Rect(w / 2 - 175, 178, 350, 80), _resultText);
                    if (GUI.Button(new Rect(w / 2 - 80, 250, 160, 26), "Kembali ke Menu"))
                    { _ = LeaveAsync(); _screen = Page.Menu; }
                }
                return;
            }

            if (UseNewLobby) return; // lobby UITK aktif → menu/connecting digambar NexusLobbyController, bukan IMGUI

            GUILayout.BeginArea(new Rect(w / 2 - 165, 28, 330, 440));
            var title = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            GUILayout.Label("Nexus Spike — Online", title);
            if (_status.Length > 0) GUILayout.Label(_status);
            GUILayout.Space(8);
            if (_screen == Page.Menu)
            {
                if (_backend != null && _backend.LoggedIn)
                {
                    var who = string.IsNullOrEmpty(_backend.Username) ? _backend.DisplayName + " (Guest)" : _backend.Username;
                    GUILayout.Label($"{who}  —  {_backend.RankTier} ({_backend.Rating})");
                    if (GUILayout.Button("Profil & Statistik", GUILayout.Height(30))) OpenProfile();
                    GUILayout.Space(4);
                }
                if (GUILayout.Button("Ranked Match (MMR)", GUILayout.Height(44))) RankedMatch();
                if (GUILayout.Button("Quick Match (2p)", GUILayout.Height(34))) QuickMatch();
                if (GUILayout.Button("Create Room", GUILayout.Height(28))) CreateRoom();
                if (GUILayout.Button("Leaderboard", GUILayout.Height(28))) OpenLeaderboard();
                if (GUILayout.Button("Logout", GUILayout.Height(26))) Logout();
            }
            else if (_screen == Page.Connecting)
            {
                if (GUILayout.Button("Cancel", GUILayout.Height(40))) { _ = LeaveAsync(); _screen = Page.Menu; }
            }
            GUILayout.EndArea();
        }
    }
}
