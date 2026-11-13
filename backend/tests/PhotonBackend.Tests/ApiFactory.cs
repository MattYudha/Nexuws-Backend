using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PhotonBackend.Api.Data;

namespace PhotonBackend.Tests;

public class ApiFactory : WebApplicationFactory<Program>
{
    protected virtual int RateLimitPerMinute => 100_000; // suite biasa: praktis tanpa batas

    public const string WebhookSecret = "test-webhook-secret";
    public const string AuthSecret = "test-auth-secret";
    public const string AdminSecret = "test-admin-secret";

    private readonly string _dbName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", "Host=unused");
        builder.UseSetting("Jwt:Secret", "test-secret-test-secret-test-secret-123");
        builder.UseSetting("Photon:WebhookSecret", WebhookSecret);
        builder.UseSetting("Photon:AuthSecret", AuthSecret);
        builder.UseSetting("Game:AllowTestAccounts", "true"); // izinkan flag IsTest di lingkungan tes
        builder.UseSetting("Admin:Secret", AdminSecret);      // aktifkan endpoint admin di tes
        builder.UseSetting("RateLimit:AuthPerMinute", RateLimitPerMinute.ToString());
        builder.UseSetting("RateLimit:GuestPerMinute", RateLimitPerMinute.ToString());
        builder.UseSetting("Game:GuestCreatePerMinuteGlobal", "100000"); // suite membuat ratusan guest
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        });
    }

    public record Login(Guid PlayerId, string DisplayName, int Rating, string Token);

    // Akun uji selalu di-flag IsTest lewat jalur tes (isTest=true) — tak bergantung tebakan awalan.
    public async Task<(HttpClient Client, Login Login)> LoginAsync(string deviceId, string name, bool isTest = true)
    {
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/guest", new { deviceId, displayName = name, isTest });
        res.EnsureSuccessStatusCode();
        var login = (await res.Content.ReadFromJsonAsync<Login>(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        return (client, login);
    }

    public async Task<bool> IsTestAccountAsync(Guid playerId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var p = await db.Players.FindAsync(playerId);
        return p?.IsTestAccount ?? false;
    }

    /// <summary>Seed N match ranked SELESAI untuk pemain (uji jendela placement).</summary>
    public async Task SeedFinishedRankedAsync(Guid playerId, int count)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for (int i = 0; i < count; i++)
        {
            var m = new Match { GameId = "seed-" + Guid.NewGuid().ToString("N"), Mode = "ranked", Status = MatchStatus.Finished };
            m.Players.Add(new MatchPlayer { PlayerId = playerId, Slot = 0 });
            db.Matches.Add(m);
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Jejak audit perubahan faksi oleh admin untuk pemain ini (urut waktu).</summary>
    public async Task<List<FactionAdminAudit>> FactionAuditsAsync(Guid playerId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.FactionAdminAudits
            .Where(a => a.PlayerId == playerId)
            .OrderBy(a => a.At)
            .ToListAsync();
    }
}

/// <summary>Q61: factory dengan batas rate limit kecil untuk menguji 429.</summary>
public sealed class RateLimitedFactory : ApiFactory
{
    protected override int RateLimitPerMinute => 3;
}
