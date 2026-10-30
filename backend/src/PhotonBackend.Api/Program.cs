using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PhotonBackend.Api;
using PhotonBackend.Api.Auth;
using PhotonBackend.Api.Data;
using PhotonBackend.Api.Endpoints;
using PhotonBackend.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));
builder.Services.Configure<PhotonOptions>(builder.Configuration.GetSection(PhotonOptions.Section));
builder.Services.Configure<GameOptions>(builder.Configuration.GetSection(GameOptions.Section));

var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
if (jwt.Secret.Length < 32)
    throw new InvalidOperationException("Jwt:Secret must be set and at least 32 characters (env var Jwt__Secret).");

// Dev tanpa Postgres: Database:UseInMemory=true → DB in-memory (data hilang saat restart).
// Produksi/real: Npgsql. (Tes integrasi memakai InMemory lewat ApiFactory.)
var useInMemory = builder.Configuration.GetValue("Database:UseInMemory", false);
builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (useInMemory)
        o.UseInMemoryDatabase("nexus-dev");
    else
        o.UseNpgsql(builder.Configuration.GetConnectionString("Default")
                    ?? throw new InvalidOperationException("ConnectionStrings:Default is not set."));
});

builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<MatchmakingService>();
builder.Services.AddSingleton<LeaderboardService>();
builder.Services.AddSingleton<FactionService>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Identity.PasswordHasher<PhotonBackend.Api.Data.Player>>();
builder.Services.AddScoped<MatchService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenService(Options.Create(jwt)).ValidationParameters;
    });
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational()) db.Database.Migrate();
    else db.Database.EnsureCreated();

    // Backfill: match lama (sebelum AddMatchMeta) Mode kosong → "ranked" (spike awal = ranked 1v1).
    var stale = db.Matches.Where(m => m.Status == PhotonBackend.Api.Data.MatchStatus.Finished && (m.Mode == null || m.Mode == "")).ToList();
    foreach (var m in stale) m.Mode = "ranked";
    if (stale.Count > 0) db.SaveChanges();

    // Backfill format: match Finished tanpa Format → "legacy5v5" (format lama 5v5-bot; K/D TIDAK valid utk ranked).
    var noFmt = db.Matches.Where(m => m.Status == PhotonBackend.Api.Data.MatchStatus.Finished && (m.Format == null || m.Format == "")).ToList();
    foreach (var m in noFmt) m.Format = "legacy5v5";
    if (noFmt.Count > 0) db.SaveChanges();

    // Backfill SATU KALI: tandai akun uji LAMA berdasar NAMA (E2E_*/FB_*/GATE/PersistTest/logintest_*).
    // Hanya menandai (tak pernah membatalkan). Akun baru TIDAK tersentuh aturan ini (flag via jalur tes eksplisit).
    var toFlag = db.Players.Where(p => !p.IsTestAccount).ToList()
        .Where(p => PhotonBackend.Api.Services.Ranks.IsLegacyTestName(p.Username, p.DisplayName)).ToList();
    foreach (var p in toFlag) p.IsTestAccount = true;
    if (toFlag.Count > 0) db.SaveChanges();

    // Backfill proteksi demosi: set LastRankedTierOrder = tier mentah terkini untuk akun lama (default 0),
    // supaya match berikutnya tak salah dibaca sebagai "promosi". Idempoten (hanya menyentuh yang masih 0).
    var noFloor = db.Players.Where(p => p.LastRankedTierOrder == 0).ToList()
        .Where(p => PhotonBackend.Api.Services.Ranks.TierOrder(PhotonBackend.Api.Services.Ranks.TierFor(p.Rating)) > 0).ToList();
    foreach (var p in noFloor) p.LastRankedTierOrder = PhotonBackend.Api.Services.Ranks.TierOrder(PhotonBackend.Api.Services.Ranks.TierFor(p.Rating));
    if (noFloor.Count > 0) db.SaveChanges();

    // Proteksi demosi NONAKTIF (DemotionGraceMatches=0) → bersihkan floor yang mungkin masih aktif
    // (akun uji dari masa percobaan). Idempoten. Hapus blok ini TIDAK perlu saat fitur diaktifkan lagi.
    if (app.Configuration.GetValue("Game:DemotionGraceMatches", 0) <= 0)
    {
        var activeFloors = db.Players.Where(p => p.TierFloorLeft > 0 || p.TierFloorOrder > 0).ToList();
        foreach (var p in activeFloors) { p.TierFloorLeft = 0; p.TierFloorOrder = 0; }
        if (activeFloors.Count > 0) db.SaveChanges();
    }
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapApi();
app.MapPhoton();

app.Run();

public partial class Program;
