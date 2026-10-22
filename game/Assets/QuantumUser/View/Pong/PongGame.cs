using System;
using System.Threading.Tasks;
using Photon.Deterministic;
using Photon.Realtime;
using Quantum;
using UnityEngine;

namespace Pong
{
    /// <summary>
    /// Game flow: guest login against the backend -> matchmaking on Photon Cloud -> Quantum session
    /// -> result report back to the backend. UI is immediate-mode (OnGUI) so the sample needs no
    /// prefabs or UI packages.
    /// </summary>
    public unsafe class PongGame : MonoBehaviour
    {
        private enum Page { Login, Menu, Connecting, InMatch, Result, Leaderboard }

        private const string BackendUrlKey = "pong.backendUrl";
        private const string NameKey = "pong.name";

        [Tooltip("Base URL of the Pong backend. 10.0.2.2 is the host machine from the Android emulator.")]
        public string BackendUrl = "http://10.0.2.2:8080";

        [Tooltip("Assign the Map asset (and SimulationConfig if needed) created by Quantum Hub.")]
        public RuntimeConfig RuntimeConfig = new RuntimeConfig();

        public static int LocalSlot { get; private set; } = -1;

        private BackendClient _backend;
        private RealtimeClient _client;
        private IDisposable _pluginDisconnectSubscription;
        private Page _screen = Page.Login;
        private string _status = "";
        private string _name = "";
        private string _roomCode = "";
        private string _gameId = "";
        private bool _reported;
        private bool _won;
        private int _finalLeft, _finalRight;
        private MeResponse _me;
        private LeaderboardRow[] _leaderboard = Array.Empty<LeaderboardRow>();

        public static Frame PredictedFrame()
        {
            var runner = QuantumRunner.Default;
            if (runner == null || runner.Game == null) return null;
            return runner.Game.Frames.Predicted;
        }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            BackendUrl = PlayerPrefs.GetString(BackendUrlKey, BackendUrl);
            _name = PlayerPrefs.GetString(NameKey, "Player" + UnityEngine.Random.Range(100, 999));
            _backend = new BackendClient(BackendUrl);
        }

        // ---------------------------------------------------------------- flow

        private async void Login()
        {
            _status = "Signing in...";
            try
            {
                PlayerPrefs.SetString(BackendUrlKey, BackendUrl);
                PlayerPrefs.SetString(NameKey, _name);
                _backend.BaseUrl = BackendUrl.TrimEnd('/');
                await _backend.LoginAsync(_name.Trim());
                await RefreshMe();
                _status = "";
                _screen = Page.Menu;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _status = "Cannot reach the backend: " + e.Message;
            }
        }

        private async Task RefreshMe()
        {
            try { _me = await _backend.GetMeAsync(); }
            catch (Exception e) { Debug.LogWarning(e.Message); }
        }

        // RoomName == null makes Photon pick a random open room, or create one (RandomMatchingType FillRoom).
        private async void QuickMatch() => await ConnectWithErrorHandling(null, canOnlyJoin: false);

        private async void CreateRoom()
        {
            _roomCode = UnityEngine.Random.Range(1000, 9999).ToString();
            await ConnectWithErrorHandling("room-" + _roomCode, canOnlyJoin: false);
        }

        private async void JoinRoom() => await ConnectWithErrorHandling("room-" + _roomCode.Trim(), canOnlyJoin: true);

        private async Task ConnectWithErrorHandling(string roomName, bool canOnlyJoin)
        {
            _screen = Page.Connecting;
            _status = roomName == null ? "Looking for an opponent..." : "Connecting...";
            try
            {
                await StartSessionAsync(roomName, canOnlyJoin);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                await LeaveAsync(toMenu: false);
                _status = "Could not start the match: " + e.Message;
                _screen = Page.Menu;
            }
        }

        private async Task StartSessionAsync(string roomName, bool canOnlyJoin)
        {
            // Photon calls the backend's /photon/auth with this token (custom authentication).
            var auth = new AuthenticationValues { AuthType = CustomAuthenticationType.Custom };
            auth.AddAuthParameter("token", _backend.Session.token);

            var connectionArguments = new MatchmakingArguments
            {
                PhotonSettings = new AppSettings(PhotonServerSettings.Global.AppSettings),
                PluginName = "QuantumPlugin",
                RoomName = roomName,
                MaxPlayers = Quantum.Input.MAX_COUNT,
                CanOnlyJoin = canOnlyJoin,
                AuthValues = auth,
            };
            _client = await MatchmakingExtensions.ConnectToRoomAsync(connectionArguments);

            var runtimeConfig = RuntimeConfig ?? new RuntimeConfig();
            var sessionRunnerArguments = new SessionRunner.Arguments
            {
                RunnerFactory = QuantumRunnerUnityFactory.DefaultFactory,
                GameParameters = QuantumRunnerUnityFactory.CreateGameParameters,
                ClientId = _client.UserId,
                RuntimeConfig = runtimeConfig,
                SessionConfig = QuantumDeterministicSessionConfigAsset.DefaultConfig,
                GameMode = DeterministicGameMode.Multiplayer,
                PlayerCount = Quantum.Input.MAX_COUNT,
                StartGameTimeoutInSeconds = 10,
                Communicator = new QuantumNetworkCommunicator(_client),
            };
            // CallbackPluginDisconnect can fire during StartAsync, so subscribe first (same order as the SDK sample).
            _pluginDisconnectSubscription = QuantumCallback.SubscribeManual<CallbackPluginDisconnect>(
                callback => { _ = LeaveAsync(toMenu: true); });

            var runner = (QuantumRunner)await SessionRunner.StartAsync(sessionRunnerArguments);
            runner.Game.AddPlayer(new RuntimePlayer { PlayerNickname = _backend.Session.displayName });

            _gameId = _client.CurrentRoom?.Name ?? "";
            _reported = false;
            LocalSlot = -1;
            _status = "";
            _screen = Page.InMatch;
        }

        private async Task LeaveAsync(bool toMenu)
        {
            _pluginDisconnectSubscription?.Dispose();
            _pluginDisconnectSubscription = null;

            try { await QuantumRunner.ShutdownAllAsync(); }
            catch (Exception e) { Debug.LogWarning(e.Message); }
            try { if (_client != null) await _client.DisconnectAsync(); }
            catch (Exception e) { Debug.LogWarning(e.Message); }
            _client = null;
            LocalSlot = -1;
            if (toMenu)
            {
                await RefreshMe();
                _screen = Page.Menu;
            }
        }

        private async void ShowLeaderboard()
        {
            _status = "Loading...";
            try { _leaderboard = await _backend.GetLeaderboardAsync(10); _status = ""; }
            catch (Exception e) { _status = e.Message; }
            _screen = Page.Leaderboard;
        }

        private async void ReportResult(int left, int right)
        {
            try
            {
                await _backend.ReportResultAsync(_gameId, LocalSlot, left, right);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Result report failed: " + e.Message);
            }
        }

        // -------------------------------------------------------------- update

        private void Update()
        {
            if (_screen != Page.InMatch) return;
            var runner = QuantumRunner.Default;
            if (runner == null || runner.Game == null) return;

            if (LocalSlot < 0)
            {
                for (int slot = 0; slot < Quantum.Input.MAX_COUNT; slot++)
                    if (runner.Game.PlayerIsLocal(slot)) { LocalSlot = slot; break; }
            }

            // Only the verified frame is final; predicted frames can still be rolled back.
            var verified = runner.Game.Frames.Verified;
            if (verified == null || LocalSlot < 0 || _reported) return;
            if (verified.Global->State != PongSystem.StateOver) return;

            _reported = true;
            _finalLeft = verified.Global->ScoreLeft;
            _finalRight = verified.Global->ScoreRight;
            _won = (LocalSlot == 0) == (_finalLeft > _finalRight);
            ReportResult(_finalLeft, _finalRight);
            _screen = Page.Result;
        }

        // ------------------------------------------------------------------ UI

        private void OnGUI()
        {
            float scale = Mathf.Max(1f, UnityEngine.Screen.height / 540f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float width = UnityEngine.Screen.width / scale;
            float height = UnityEngine.Screen.height / scale;

            switch (_screen)
            {
                case Page.InMatch: DrawHud(width); break;
                default:
                    GUILayout.BeginArea(new Rect(width / 2 - 170, 20, 340, height - 40));
                    DrawMenus();
                    GUILayout.EndArea();
                    break;
            }
        }

        private void DrawHud(float width)
        {
            var frame = PredictedFrame();
            if (frame == null) return;
            var g = frame.Global;

            var big = new GUIStyle(GUI.skin.label) { fontSize = 40, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(width / 2 - 100, 8, 200, 50), $"{g->ScoreLeft}  :  {g->ScoreRight}", big);

            var small = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.UpperCenter };
            string hint = g->State == PongSystem.StateWaiting
                ? $"Waiting for opponent...  room: {_gameId}"
                : g->CountdownTicks > 0 ? $"Get ready... {g->CountdownTicks / 60 + 1}" : "";
            if (LocalSlot >= 0) hint += (hint.Length > 0 ? "\n" : "") + (LocalSlot == 0 ? "You are LEFT (blue)" : "You are RIGHT (orange)");
            GUI.Label(new Rect(width / 2 - 200, 60, 400, 60), hint, small);

            if (GUI.Button(new Rect(10, 10, 80, 36), "Leave")) _ = LeaveAsync(toMenu: true);
        }

        private void DrawMenus()
        {
            var title = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            GUILayout.Label("Photon Pong", title);
            if (_status.Length > 0) GUILayout.Label(_status);
            GUILayout.Space(8);

            switch (_screen)
            {
                case Page.Login:
                    GUILayout.Label("Backend URL");
                    BackendUrl = GUILayout.TextField(BackendUrl);
                    GUILayout.Label("Display name");
                    _name = GUILayout.TextField(_name, 24);
                    if (GUILayout.Button("Play as guest", GUILayout.Height(44)) && _name.Trim().Length > 0) Login();
                    break;

                case Page.Menu:
                    if (_me != null) GUILayout.Label($"{_me.displayName}   rating {_me.rating}   W {_me.wins} / L {_me.losses}");
                    if (GUILayout.Button("Quick match", GUILayout.Height(44))) QuickMatch();
                    if (GUILayout.Button("Create private room", GUILayout.Height(44))) CreateRoom();
                    GUILayout.BeginHorizontal();
                    _roomCode = GUILayout.TextField(_roomCode, 6, GUILayout.Width(100), GUILayout.Height(44));
                    if (GUILayout.Button("Join room code", GUILayout.Height(44)) && _roomCode.Trim().Length > 0) JoinRoom();
                    GUILayout.EndHorizontal();
                    if (GUILayout.Button("Leaderboard", GUILayout.Height(44))) ShowLeaderboard();
                    break;

                case Page.Connecting:
                    if (GUILayout.Button("Cancel", GUILayout.Height(44))) { _ = LeaveAsync(toMenu: true); }
                    break;

                case Page.Result:
                    var big = new GUIStyle(GUI.skin.label) { fontSize = 32, alignment = TextAnchor.MiddleCenter };
                    GUILayout.Label(_won ? "You win!" : "You lose", big);
                    GUILayout.Label($"Final score {_finalLeft} : {_finalRight}", big);
                    if (GUILayout.Button("Back to menu", GUILayout.Height(44))) _ = LeaveAsync(toMenu: true);
                    break;

                case Page.Leaderboard:
                    for (int i = 0; i < _leaderboard.Length; i++)
                        GUILayout.Label($"{i + 1}. {_leaderboard[i].displayName}   {_leaderboard[i].rating}   ({_leaderboard[i].wins}W/{_leaderboard[i].losses}L)");
                    if (GUILayout.Button("Back", GUILayout.Height(44))) _screen = Page.Menu;
                    break;
            }
        }
    }
}
