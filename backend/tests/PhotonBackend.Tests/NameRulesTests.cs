using PhotonBackend.Api.Services;

namespace PhotonBackend.Tests;

// D14: nama terlarang (anti impersonasi staf/sistem) + normalisasi case-insensitive.
public class NameRulesTests
{
    [Theory]
    [InlineData("admin")] [InlineData("ADMIN")] [InlineData("Administrator")] [InlineData("G.M")] [InlineData("gm")]
    [InlineData("GM_Budi")] [InlineData("Admin123")] [InlineData("[GM]Rio")] [InlineData("Staff Ana")] [InlineData("Nexus Saga")]
    [InlineData("Moderator")] [InlineData("System")] [InlineData("   ")]
    public void Reserved(string name) => Assert.True(NameRules.IsReserved(name));

    [Theory]
    [InlineData("Budi")] [InlineData("Gman")] [InlineData("Gmail")] [InlineData("Administrasi")] [InlineData("Guest4821")] [InlineData("RioGM")]
    public void Allowed(string name) => Assert.False(NameRules.IsReserved(name));

    // Q86: homoglyph Kiril/Yunani tidak lolos larangan staf.
    [Theory]
    [InlineData("Аdmin")]            // А Kiril + "dmin"
    [InlineData("Аԁmin")]      // А + ԁ Kiril (keduanya mirip Latin)
    [InlineData("ＧＭ")]         // ＧＭ lebar penuh
    [InlineData("Stаff")]            // а Kiril di tengah
    public void Reserved_HomoglyphsAndFullwidth(string name) => Assert.True(NameRules.IsReserved(name));

    // Q87: lebar penuh & aksen disatukan untuk keunikan.
    [Theory]
    [InlineData("Ｂｕｄｉ")]   // Ｂｕｄｉ
    [InlineData("Búdi")]                     // Búdi
    [InlineData("Вudi")]                     // В Kiril
    public void Normalize_FoldsLookalikesToSameKey(string name) => Assert.Equal(NameRules.Normalize("budi"), NameRules.Normalize(name));

    // Q89: simbol semua = bukan huruf/angka.
    [Fact]
    public void SymbolsOnly_HasNoLetterOrDigit() => Assert.False(NameRules.HasLetterOrDigit("★★★"));

    [Fact]
    public void Normalize_IsCaseInsensitiveAndTrimmed() => Assert.Equal(NameRules.Normalize(" BuDi "), NameRules.Normalize("budi"));
}
