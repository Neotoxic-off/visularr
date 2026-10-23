using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Api.Models.UserDtos;
using Jellyfin.Extensions.Json;
using Xunit;

namespace Jellyfin.Server.Integration.Tests.Controllers;

public sealed class SystemControllerTests : IClassFixture<JellyfinApplicationFactory>
{
    private readonly JellyfinApplicationFactory _factory;

    public SystemControllerTests(JellyfinApplicationFactory factory)
    {
        _factory = factory;
    }

    // The test server's requests come from a local address: restarting must still need an administrator.
    [Fact]
    public async Task Restart_LocalNonAdmin_Forbidden()
    {
        JsonSerializerOptions jsonOptions = JsonDefaults.Options;
        HttpClient admin = _factory.CreateClient();
        admin.DefaultRequestHeaders.AddAuthHeader(await AuthHelper.CompleteStartupAsync(admin));
        using HttpResponseMessage created = await admin.PostAsJsonAsync("Users/New", new CreateUserByName { Name = "lanmember", Password = "secret-pw" }, jsonOptions);
        created.EnsureSuccessStatusCode();

        HttpClient member = _factory.CreateClient();
        using HttpRequestMessage login = new HttpRequestMessage(HttpMethod.Post, "Users/AuthenticateByName");
        login.Headers.TryAddWithoutValidation(AuthHelper.AuthHeaderName, AuthHelper.DummyAuthHeader);
        login.Content = JsonContent.Create(new AuthenticateUserByName { Username = "lanmember", Pw = "secret-pw" }, options: jsonOptions);
        using HttpResponseMessage authenticated = await member.SendAsync(login);
        authenticated.EnsureSuccessStatusCode();
        JsonElement result = await authenticated.Content.ReadFromJsonAsync<JsonElement>(jsonOptions);
        member.DefaultRequestHeaders.AddAuthHeader(result.GetProperty("AccessToken").GetString()!);

        using HttpResponseMessage response = await member.PostAsync("System/Restart", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
