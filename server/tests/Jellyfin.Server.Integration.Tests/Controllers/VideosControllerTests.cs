using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Jellyfin.Server.Integration.Tests.Controllers;

public sealed class VideosControllerTests : IClassFixture<JellyfinApplicationFactory>
{
    private readonly JellyfinApplicationFactory _factory;
    private static string? _accessToken;

    public VideosControllerTests(JellyfinApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DeleteAlternateSources_NonexistentItemId_NotFound()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        HttpResponseMessage response = await client.DeleteAsync($"Videos/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetVideoStream_ApiKeyQuery_PassesAuthorization()
    {
        HttpClient client = _factory.CreateClient();
        _accessToken ??= await AuthHelper.CompleteStartupAsync(client);

        using HttpResponseMessage response = await client.GetAsync($"Videos/{Guid.NewGuid()}/stream?api_key={_accessToken}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "Videos/{0}/stream")]
    [InlineData("HEAD", "Videos/{0}/stream")]
    [InlineData("GET", "Videos/{0}/stream.mkv")]
    [InlineData("HEAD", "Videos/{0}/stream.mkv")]
    [InlineData("GET", "Audio/{0}/stream")]
    [InlineData("GET", "Audio/{0}/stream.mp3")]
    [InlineData("GET", "Videos/{0}/{0}/Subtitles/0/Stream.srt")]
    [InlineData("GET", "Videos/{0}/{0}/Subtitles/0/0/Stream.srt")]
    [InlineData("GET", "Videos/{0}/{0}/Attachments/0")]
    [InlineData("GET", "Videos/{0}/master.m3u8")]
    [InlineData("GET", "Audio/{0}/universal")]
    [InlineData("POST", "System/Restart")]
    public async Task Anonymous_MediaAndRestart_Unauthorized(string method, string format)
    {
        HttpClient client = _factory.CreateClient();

        using HttpRequestMessage request = new HttpRequestMessage(new HttpMethod(method), string.Format(CultureInfo.InvariantCulture, format, Guid.NewGuid()));
        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
