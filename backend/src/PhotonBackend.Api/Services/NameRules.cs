using System.Globalization;
using System.Text;

namespace PhotonBackend.Api.Services;

/// <summary>
/// D14 (2026-11-13): nama tampil UNIK tanpa membedakan huruf besar-kecil + nama terlarang (anti impersonasi staf/sistem).
/// Q86/Q87: perbandingan memakai "skeleton" — NFKC (lebar penuh → biasa), aksen dibuang, huruf Kiril/Yunani yang mirip
/// Latin dipetakan ke Latin — agar "Аdmin" (A Kiril), "Ｂｕｄｉ", "Búdi" tidak lolos sebagai nama berbeda.
/// </summary>
public static class NameRules
{
    // Huruf non-Latin yang tampak identik dengan huruf Latin (subset confusables UTS #39 yang relevan untuk nama).
    private static readonly Dictionary<char, char> Confusables = new()
    {
        // Kiril
        ['а'] = 'a', ['в'] = 'b', ['с'] = 'c', ['е'] = 'e', ['ё'] = 'e', ['һ'] = 'h', ['і'] = 'i', ['ї'] = 'i', ['ј'] = 'j',
        ['к'] = 'k', ['м'] = 'm', ['н'] = 'h', ['о'] = 'o', ['р'] = 'p', ['ԛ'] = 'q', ['ѕ'] = 's', ['т'] = 't', ['у'] = 'y',
        ['х'] = 'x', ['ԝ'] = 'w', ['ү'] = 'y', ['ӏ'] = 'l', ['ԁ'] = 'd', ['ɡ'] = 'g',
        // Yunani
        ['α'] = 'a', ['β'] = 'b', ['ε'] = 'e', ['η'] = 'n', ['ι'] = 'i', ['κ'] = 'k', ['ν'] = 'v', ['ο'] = 'o', ['ρ'] = 'p',
        ['τ'] = 't', ['υ'] = 'u', ['χ'] = 'x', ['ω'] = 'w', ['μ'] = 'u',
    };

    /// <summary>
    /// Bentuk pembanding: NFKC → huruf kecil → buang tanda diakritik → petakan confusable ke Latin → trim.
    /// Dipakai sebagai kunci keunikan (<c>Player.NormalizedName</c>) dan untuk cek nama terlarang.
    /// Untuk nama ASCII hasilnya = huruf kecil (data lama tetap cocok).
    /// </summary>
    public static string Normalize(string name)
    {
        var nfkc = name.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var sb = new StringBuilder(nfkc.Length);
        foreach (var ch in nfkc.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue; // aksen
            sb.Append(Confusables.TryGetValue(ch, out var latin) ? latin : ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    // Dibandingkan pada bentuk "ringkas" (huruf+angka saja dari skeleton), jadi "G.M", "g_m", "ADMIN 1", "Аdmin" tertangkap.
    private static readonly HashSet<string> Exact = new(StringComparer.Ordinal)
    {
        "admin", "administrator", "gm", "gamemaster", "mod", "moderator", "staff", "support", "cs", "dev", "developer",
        "system", "sistem", "official", "resmi", "owner", "nexus", "nexussaga", "server", "bot", "guest",
    };
    private static readonly string[] Prefixes = { "admin", "gm", "moderator", "staff", "official", "nexusofficial", "system", "sistem" };

    private static string Compact(string skeleton)
    {
        var sb = new StringBuilder(skeleton.Length);
        foreach (var ch in skeleton) if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }

    /// <summary>Q89: nama harus memuat huruf/angka (bukan simbol semua seperti "★★★").</summary>
    public static bool HasLetterOrDigit(string name) => name.Any(char.IsLetterOrDigit);

    /// <summary>
    /// Terlarang bila bentuk ringkas = kata terlarang, atau diawali awalan staf diikuti angka/pemisah
    /// (mis. "GM_Budi", "Admin123", "[GM]Rio"). "Gman" / "Gmail" tetap boleh (huruf langsung menyambung).
    /// </summary>
    public static bool IsReserved(string name)
    {
        var skel = Normalize(name);
        var c = Compact(skel);
        if (c.Length == 0 || Exact.Contains(c)) return true;
        var lower = skel.TrimStart('[', '(', '<', '{', '_', '-', '.', ' ');
        foreach (var p in Prefixes)
        {
            if (!lower.StartsWith(p, StringComparison.Ordinal)) continue;
            if (lower.Length == p.Length) return true;
            if (!char.IsLetter(lower[p.Length])) return true;   // GM_Budi, Admin123, GM]Rio, Staff Ana
        }
        return false;
    }
}
