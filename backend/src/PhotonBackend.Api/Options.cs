namespace PhotonBackend.Api;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "photon-backend";
    public string Secret { get; set; } = "";
    public int LifetimeHours { get; set; } = 24;
}

public sealed class PhotonOptions
{
    public const string Section = "Photon";

    /// <summary>Shared secret Photon sends in the X-SecretKey header (dashboard key "WebHookSecret").</summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>Shared secret appended as ?secret= to the custom authentication URL in the dashboard.</summary>
    public string AuthSecret { get; set; } = "";
}

public sealed class GameOptions
{
    public const string Section = "Game";

    public int WinScore { get; set; } = 5;
    public int EloK { get; set; } = 32;
    public int StartRating { get; set; } = 1000;
}
