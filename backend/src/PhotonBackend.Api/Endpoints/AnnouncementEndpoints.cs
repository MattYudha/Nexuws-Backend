using Microsoft.EntityFrameworkCore;
using PhotonBackend.Api.Data;

namespace PhotonBackend.Api.Endpoints;

public sealed record AnnouncementRequest(string? Kind, string? Title, string? Body, DateTime? StartsAt = null, DateTime? EndsAt = null, string? AdminSource = null);

/// <summary>
/// Panel Pengumuman/Event lobby (docs/31 §1.7, poin d). Publik: hanya yang aktif sekarang.
/// Admin (X-Admin-Secret): buat & hapus (soft delete), sumber tercatat seperti audit faksi.
/// </summary>
public static class AnnouncementEndpoints
{
    public const int MaxActive = 5;
    private static readonly string[] Kinds = { "info", "event" };

    public static void MapAnnouncements(this RouteGroupBuilder api, RouteGroupBuilder admin)
    {
        api.MapGet("/announcements", Active);
        admin.MapPost("/announcements", Create);
        admin.MapDelete("/announcements/{id:guid}", Remove);
    }

    private static async Task<IResult> Active(AppDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var rows = await db.Announcements.AsNoTracking()
            .Where(a => !a.Removed && a.StartsAt <= now && (a.EndsAt == null || a.EndsAt > now))
            .OrderByDescending(a => a.StartsAt).ThenByDescending(a => a.CreatedAt)
            .Take(MaxActive)
            .Select(a => new { id = a.Id, kind = a.Kind, title = a.Title, body = a.Body, startsAt = a.StartsAt, endsAt = a.EndsAt })
            .ToListAsync(ct);
        return Results.Ok(rows);
    }

    private static async Task<IResult> Create(AnnouncementRequest req, HttpContext http, AppDbContext db, ILoggerFactory loggers, CancellationToken ct)
    {
        var kind = (req.Kind ?? "info").Trim().ToLowerInvariant();
        var title = req.Title?.Trim() ?? "";
        var body = req.Body?.Trim() ?? "";
        var starts = req.StartsAt?.ToUniversalTime() ?? DateTime.UtcNow;
        var ends = req.EndsAt?.ToUniversalTime();

        if (!Kinds.Contains(kind)) return Results.BadRequest(new { error = "kind harus 'info' atau 'event'." });
        if (title.Length is < 1 or > 80) return Results.BadRequest(new { error = "title 1-80 karakter." });
        if (body.Length > 400) return Results.BadRequest(new { error = "body maks 400 karakter." });
        if (ends is { } e && e <= starts) return Results.BadRequest(new { error = "endsAt harus setelah startsAt." });

        var a = new Announcement
        {
            Kind = kind, Title = title, Body = body, StartsAt = starts, EndsAt = ends,
            CreatedBy = Source(req.AdminSource, http),
        };
        db.Announcements.Add(a);
        await db.SaveChangesAsync(ct);
        loggers.CreateLogger("AdminAnnouncement").LogWarning("Announcement {Id} '{Title}' created by {Source}", a.Id, a.Title, a.CreatedBy);
        var now = DateTime.UtcNow;
        int active = await db.Announcements.CountAsync(x => !x.Removed && x.StartsAt <= now && (x.EndsAt == null || x.EndsAt > now), ct);
        string? warning = active > MaxActive ? $"{active} pengumuman aktif; lobby hanya menampilkan {MaxActive} terbaru." : null;
        return Results.Created($"/api/announcements/{a.Id}", new { id = a.Id, activeCount = active, warning });
    }

    private static async Task<IResult> Remove(Guid id, HttpContext http, AppDbContext db, ILoggerFactory loggers, CancellationToken ct)
    {
        var a = await db.Announcements.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null || a.Removed) return Results.NotFound();
        a.Removed = true;
        await db.SaveChangesAsync(ct);
        loggers.CreateLogger("AdminAnnouncement").LogWarning("Announcement {Id} removed by {Source}", a.Id, Source(null, http));
        return Results.NoContent();
    }

    private static string Source(string? label, HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var s = $"{(string.IsNullOrWhiteSpace(label) ? "shared-secret" : label.Trim())}@{ip}";
        return s.Length > 160 ? s[..160] : s;
    }
}
