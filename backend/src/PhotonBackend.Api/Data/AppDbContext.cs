using Microsoft.EntityFrameworkCore;

namespace PhotonBackend.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<MatchPlayer> MatchPlayers => Set<MatchPlayer>();
    public DbSet<MatchPlayerHero> MatchPlayerHeroes => Set<MatchPlayerHero>();
    public DbSet<FactionAdminAudit> FactionAdminAudits => Set<FactionAdminAudit>();
    public DbSet<Announcement> Announcements => Set<Announcement>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Player>(e =>
        {
            e.HasIndex(x => x.DeviceId).IsUnique();   // nullable: Postgres izinkan banyak NULL (akun username)
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.NormalizedName).IsUnique();   // D14
            e.Property(x => x.NormalizedName).HasMaxLength(24);
            e.HasIndex(x => x.Rating);
            e.Property(x => x.DeviceId).HasMaxLength(128);
            e.Property(x => x.Username).HasMaxLength(24);
            e.Property(x => x.DisplayName).HasMaxLength(24);
        });

        b.Entity<Match>(e =>
        {
            e.HasIndex(x => x.GameId).IsUnique();
            e.Property(x => x.GameId).HasMaxLength(128);
            e.Property(x => x.Region).HasMaxLength(16);
            e.Property(x => x.Status).HasMaxLength(16);
        });

        b.Entity<MatchPlayer>(e =>
        {
            e.HasKey(x => new { x.MatchId, x.PlayerId });
            e.HasIndex(x => new { x.MatchId, x.Slot }).IsUnique();
            e.HasOne(x => x.Match).WithMany(m => m.Players).HasForeignKey(x => x.MatchId);
            e.HasOne(x => x.Player).WithMany().HasForeignKey(x => x.PlayerId);
        });

        b.Entity<MatchPlayerHero>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.MatchId, x.PlayerId });
            e.HasOne(x => x.MatchPlayer)
                .WithMany(mp => mp.Heroes)
                .HasForeignKey(x => new { x.MatchId, x.PlayerId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<FactionAdminAudit>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PlayerId);
            e.HasIndex(x => x.At);
            e.Property(x => x.AdminSource).HasMaxLength(160);
        });

        b.Entity<Announcement>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Removed, x.StartsAt });
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.Title).HasMaxLength(80);
            e.Property(x => x.Body).HasMaxLength(400);
            e.Property(x => x.CreatedBy).HasMaxLength(160);
        });
    }
}
