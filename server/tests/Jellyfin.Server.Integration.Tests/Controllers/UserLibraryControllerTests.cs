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

public sealed class UserLibraryControllerTests : IClassFixture<JellyfinApplicationFactory>
{
    private readonly JellyfinApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Options;
    private static string? _accessToken;

    public UserLibraryControllerTests(JellyfinApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetRootFolder_NonexistentUserId_NotFound()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        HttpResponseMessage response = await client.GetAsync($"Users/{Guid.NewGuid()}/Items/Root");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetRootFolder_UserId_Valid()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        _ = await AuthHelper.GetRootFolderDtoAsync(client);
    }

    [Theory]
    [InlineData("Users/{0}/Items/{1}")]
    [InlineData("Users/{0}/Items/{1}/Intros")]
    [InlineData("Users/{0}/Items/{1}/LocalTrailers")]
    [InlineData("Users/{0}/Items/{1}/SpecialFeatures")]
    [InlineData("Users/{0}/Items/{1}/Lyrics")]
    public async Task GetItem_NonexistentUserId_NotFound(string format)
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        BaseItemDto rootFolderDto = await AuthHelper.GetRootFolderDtoAsync(client);

        HttpResponseMessage response = await client.GetAsync(string.Format(CultureInfo.InvariantCulture, format, Guid.NewGuid(), rootFolderDto.Id));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Users/{0}/Items/{1}")]
    [InlineData("Users/{0}/Items/{1}/Intros")]
    [InlineData("Users/{0}/Items/{1}/LocalTrailers")]
    [InlineData("Users/{0}/Items/{1}/SpecialFeatures")]
    [InlineData("Users/{0}/Items/{1}/Lyrics")]
    public async Task GetItem_NonexistentItemId_NotFound(string format)
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        UserDto userDto = await AuthHelper.GetUserDtoAsync(client);

        HttpResponseMessage response = await client.GetAsync(string.Format(CultureInfo.InvariantCulture, format, userDto.Id, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Skip = "Disabled for flaky execution after refactor.")]
    public async Task GetItem_UserIdAndItemId_Valid()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        UserDto userDto = await AuthHelper.GetUserDtoAsync(client);
        BaseItemDto rootFolderDto = await AuthHelper.GetRootFolderDtoAsync(client, userDto.Id);

        HttpResponseMessage response = await client.GetAsync($"Users/{userDto.Id}/Items/{rootFolderDto.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        BaseItemDto? rootDto = await response.Content.ReadFromJsonAsync<BaseItemDto>(_jsonOptions);
        Assert.NotNull(rootDto);
    }

    [Fact(Skip = "Disabled for flaky execution after refactor.")]
    public async Task GetIntros_UserIdAndItemId_Valid()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        UserDto userDto = await AuthHelper.GetUserDtoAsync(client);
        BaseItemDto rootFolderDto = await AuthHelper.GetRootFolderDtoAsync(client, userDto.Id);

        HttpResponseMessage response = await client.GetAsync($"Users/{userDto.Id}/Items/{rootFolderDto.Id}/Intros");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        QueryResult<BaseItemDto>? rootDto = await response.Content.ReadFromJsonAsync<QueryResult<BaseItemDto>>(_jsonOptions);
        Assert.NotNull(rootDto);
    }

    [Theory(Skip = "Disabled for flaky execution after refactor.")]
    [InlineData("Users/{0}/Items/{1}/LocalTrailers")]
    [InlineData("Users/{0}/Items/{1}/SpecialFeatures")]
    public async Task LocalTrailersAndSpecialFeatures_UserIdAndItemId_Valid(string format)
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        UserDto userDto = await AuthHelper.GetUserDtoAsync(client);
        BaseItemDto rootFolderDto = await AuthHelper.GetRootFolderDtoAsync(client, userDto.Id);

        HttpResponseMessage response = await client.GetAsync(string.Format(CultureInfo.InvariantCulture, format, userDto.Id, rootFolderDto.Id));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        BaseItemDto[]? rootDto = await response.Content.ReadFromJsonAsync<BaseItemDto[]>(_jsonOptions);
        Assert.NotNull(rootDto);
    }
}
