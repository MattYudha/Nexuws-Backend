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

    [Fact]
    public void Normalize_IsCaseInsensitiveAndTrimmed() => Assert.Equal(NameRules.Normalize(" BuDi "), NameRules.Normalize("budi"));
}
