using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Pong
{
    [Serializable] public class LoginResponse { public string playerId; public string displayName; public int rating; public string token; }
    [Serializable] public class MeResponse { public string playerId; public string displayName; public int rating; public int wins; public int losses; }
    [Serializable] public class LeaderboardRow { public string displayName; public int rating; public int wins; public int losses; }
    [Serializable] public class LeaderboardResponse { public LeaderboardRow[] rows; }
    [Serializable] class GuestLoginBody { public string deviceId; public string displayName; }
    [Serializable] class ResultBody { public string gameId; public int slot; public int scoreLeft; public int scoreRight; }

    /// <summary>Thin REST client for the Pong backend (see /backend in the repository).</summary>
    public sealed class BackendClient
    {
        private const string DeviceIdKey = "pong.deviceId";

        public string BaseUrl { get; set; }
        public LoginResponse Session { get; private set; }

        public BackendClient(string baseUrl) { BaseUrl = baseUrl.TrimEnd('/'); }

        private static string DeviceId()
        {
            var id = PlayerPrefs.GetString(DeviceIdKey, "");
            if (id.Length < 8)
            {
                id = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(DeviceIdKey, id);
                PlayerPrefs.Save();
            }
            return id;
        }

        public async Task<LoginResponse> LoginAsync(string displayName)
        {
            var body = JsonUtility.ToJson(new GuestLoginBody { deviceId = DeviceId(), displayName = displayName });
            var json = await SendAsync("POST", "/api/auth/guest", body, authorize: false);
            Session = JsonUtility.FromJson<LoginResponse>(json);
            return Session;
        }

        public async Task<MeResponse> GetMeAsync()
            => JsonUtility.FromJson<MeResponse>(await SendAsync("GET", "/api/me", null, authorize: true));

        public async Task<LeaderboardRow[]> GetLeaderboardAsync(int limit = 10)
        {
            var json = await SendAsync("GET", "/api/leaderboard?limit=" + limit, null, authorize: false);
            // JsonUtility cannot parse a top-level array, so wrap it.
            return JsonUtility.FromJson<LeaderboardResponse>("{\"rows\":" + json + "}").rows;
        }

        public Task<string> ReportResultAsync(string gameId, int slot, int scoreLeft, int scoreRight)
        {
            var body = JsonUtility.ToJson(new ResultBody { gameId = gameId, slot = slot, scoreLeft = scoreLeft, scoreRight = scoreRight });
            return SendAsync("POST", "/api/matches/result", body, authorize: true);
        }

        private async Task<string> SendAsync(string method, string path, string jsonBody, bool authorize)
        {
            using var request = new UnityWebRequest(BaseUrl + path, method);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 15;
            if (jsonBody != null)
            {
                request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonBody));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            if (authorize && Session != null)
                request.SetRequestHeader("Authorization", "Bearer " + Session.token);

            var done = new TaskCompletionSource<bool>();
            request.SendWebRequest().completed += _ => done.TrySetResult(true);
            await done.Task;

            if (request.result != UnityWebRequest.Result.Success)
                throw new Exception($"{method} {path} failed: {request.responseCode} {request.error} {request.downloadHandler?.text}");
            return request.downloadHandler.text;
        }
    }
}
