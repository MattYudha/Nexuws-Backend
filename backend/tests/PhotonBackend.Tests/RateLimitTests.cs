using System.Net;
using System.Net.Http.Json;

namespace PhotonBackend.Tests;

// Q61: batas per IP per menit pada /auth/guest dan /auth/login (anti flood & brute force).
public class RateLimitTests(RateLimitedFactory factory) : IClassFixture<RateLimitedFactory>
{
    [Fact]
    public async Task GuestLogin_Over_Limit_Returns429()
    {
        var c = factory.CreateClient();
        for (int i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/auth/guest", new { deviceId = $"device-rl-{i:000}", displayName = "RL" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await c.PostAsJsonAsync("/api/auth/guest", new { deviceId = "device-rl-999", displayName = "RL" })).StatusCode);
    }

    [Fact]
    public async Task Login_Over_Limit_Returns429()
    {
        var c = factory.CreateClient();
        HttpStatusCode last = HttpStatusCode.OK;
        for (int i = 0; i < 4; i++)
            last = (await c.PostAsJsonAsync("/api/auth/login", new { username = "nobody_here", password = "wrongpass" })).StatusCode;
        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }
}
