using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Jellyfin.Api.Models.UserDtos;
using Xunit;

namespace Jellyfin.Server.Integration.Tests;

public sealed class RateLimitTests : IClassFixture<JellyfinApplicationFactory>
{
    private readonly JellyfinApplicationFactory _factory;

    public RateLimitTests(JellyfinApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AuthenticateByName_EleventhAttemptInAMinute_TooManyRequests()
    {
        HttpClient client = _factory.CreateClient();
        for (int i = 0; i < 10; i++)
        {
            using HttpResponseMessage attempt = await PostLoginAsync(client);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, attempt.StatusCode);
        }

        using HttpResponseMessage rejected = await PostLoginAsync(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        // Other endpoints are not limited.
        using HttpResponseMessage other = await client.GetAsync("/System/Info/Public");
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostLoginAsync(HttpClient client)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/Users/AuthenticateByName");
        request.Headers.TryAddWithoutValidation(AuthHelper.AuthHeaderName, AuthHelper.DummyAuthHeader);
        request.Content = JsonContent.Create(new AuthenticateUserByName { Username = "nobody", Pw = "wrong" });
        return await client.SendAsync(request);
    }
}
