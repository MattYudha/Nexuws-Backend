using System.Text;

namespace PhotonBackend.Api.Services;

/// <summary>
/// D14 (2026-11-13): nama tampil UNIK tanpa membedakan huruf besar-kecil + nama terlarang (anti impersonasi staf/sistem).
/// </summary>
public static class NameRules
{
    /// <summary>Kunci keunikan: trim + huruf kecil (invariant). Disimpan di <c>Player.NormalizedName</c> (index unik).</summary>
    public static string Normalize(string name) => name.Trim().ToLowerInvariant();

    // Dibandingkan pada bentuk "ringkas" (huruf+angka saja), jadi "G.M", "g_m", "ADMIN 1" ikut tertangkap.
    private static readonly HashSet<string> Exact = new(StringComparer.Ordinal)
    {
        "admin", "administrator", "gm", "gamemaster", "mod", "moderator", "staff", "support", "cs", "dev", "developer",
        "system", "sistem", "official", "resmi", "owner", "nexus", "nexussaga", "server", "bot", "guest",
    };
    private static readonly string[] Prefixes = { "admin", "gm", "moderator", "staff", "official", "nexusofficial", "system", "sistem" };

    private static string Compact(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name.ToLowerInvariant()) if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }

    /// <summary>
    /// Terlarang bila bentuk ringkas = kata terlarang, atau diawali awalan staf diikuti angka/pemisah
    /// (mis. "GM_Budi", "Admin123", "[GM]Rio"). "Gman" / "Gmail" tetap boleh (huruf langsung menyambung).
    /// </summary>
    public static bool IsReserved(string name)
    {
        var c = Compact(name);
        if (c.Length == 0 || Exact.Contains(c)) return true;
        var lower = name.Trim().ToLowerInvariant().TrimStart('[', '(', '<', '{', '_', '-', '.', ' ');
        foreach (var p in Prefixes)
        {
            if (!lower.StartsWith(p, StringComparison.Ordinal)) continue;
            if (lower.Length == p.Length) return true;
            char next = lower[p.Length];
            if (!char.IsLetter(next)) return true;   // GM_Budi, Admin123, GM]Rio, Staff Ana
        }
        return false;
    }
}
