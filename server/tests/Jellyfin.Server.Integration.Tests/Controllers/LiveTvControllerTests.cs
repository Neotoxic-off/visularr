using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using MediaBrowser.Model.LiveTv;
using Xunit;

namespace Jellyfin.Server.Integration.Tests.Controllers;

public sealed class LiveTvControllerTests : IClassFixture<JellyfinApplicationFactory>
{
    private readonly JellyfinApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Options;
    private static string? _accessToken;

    public LiveTvControllerTests(JellyfinApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AddTunerHost_Unauthorized_ReturnsUnauthorized()
    {
        HttpClient client = _factory.CreateClient();

        TunerHostInfo body = new TunerHostInfo()
        {
            Type = "m3u",
            Url = "Test Data/dummy.m3u8"
        };

        HttpResponseMessage response = await client.PostAsJsonAsync("/LiveTv/TunerHosts", body, _jsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AddTunerHost_Valid_ReturnsCorrectResponse()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        TunerHostInfo body = new TunerHostInfo()
        {
            Type = "m3u",
            Url = "Test Data/dummy.m3u8"
        };

        HttpResponseMessage response = await client.PostAsJsonAsync("/LiveTv/TunerHosts", body, _jsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Encoding.UTF8.BodyName, response.Content.Headers.ContentType?.CharSet);
        TunerHostInfo? responseBody = await response.Content.ReadFromJsonAsync<TunerHostInfo>();
        Assert.NotNull(responseBody);
        Assert.Equal(body.Type, responseBody.Type);
        Assert.Equal(body.Url, responseBody.Url);
    }

    [Fact]
    public async Task AddTunerHost_InvalidType_ReturnsNotFound()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        TunerHostInfo body = new TunerHostInfo()
        {
            Type = "invalid",
            Url = "Test Data/dummy.m3u8"
        };

        HttpResponseMessage response = await client.PostAsJsonAsync("/LiveTv/TunerHosts", body, _jsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddTunerHost_InvalidUrl_ReturnsNotFound()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        TunerHostInfo body = new TunerHostInfo()
        {
            Type = "m3u",
            Url = "thisgoesnowhere"
        };

        HttpResponseMessage response = await client.PostAsJsonAsync("/LiveTv/TunerHosts", body, _jsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
