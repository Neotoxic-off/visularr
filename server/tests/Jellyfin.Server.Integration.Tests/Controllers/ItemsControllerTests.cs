using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Xunit;

namespace Jellyfin.Server.Integration.Tests.Controllers;

public sealed class ItemsControllerTests : IClassFixture<JellyfinApplicationFactory>
{
    private readonly JellyfinApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Options;
    private static string? _accessToken;

    public ItemsControllerTests(JellyfinApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetItems_NoApiKeyOrUserId_Success()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        HttpResponseMessage response = await client.GetAsync("Items");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("Users/{0}/Items")]
    [InlineData("Users/{0}/Items/Resume")]
    public async Task GetUserItems_NonexistentUserId_NotFound(string format)
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        HttpResponseMessage response = await client.GetAsync(string.Format(CultureInfo.InvariantCulture, format, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Items?userId={0}")]
    [InlineData("Users/{0}/Items")]
    [InlineData("Users/{0}/Items/Resume")]
    public async Task GetItems_UserId_Ok(string format)
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        UserDto userDto = await AuthHelper.GetUserDtoAsync(client);

        HttpResponseMessage response = await client.GetAsync(string.Format(CultureInfo.InvariantCulture, format, userDto.Id));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        QueryResult<BaseItemDto>? items = await response.Content.ReadFromJsonAsync<QueryResult<BaseItemDto>>(_jsonOptions);
        Assert.NotNull(items);
    }
}
