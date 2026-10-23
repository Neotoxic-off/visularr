using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Api.Models.LibraryStructureDto;
using Jellyfin.Extensions.Json;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Xunit;
using Xunit.Priority;

namespace Jellyfin.Server.Integration.Tests.Controllers;

[TestCaseOrderer(PriorityOrderer.Name, PriorityOrderer.Assembly)]
public sealed class LibraryStructureControllerTests : IClassFixture<JellyfinApplicationFactory>
{
    private readonly JellyfinApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Options;
    private static string? _accessToken;

    public LibraryStructureControllerTests(JellyfinApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    [Priority(-1)]
    public async Task Post_NewVirtualFolder_NotFound()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        AddVirtualFolderDto body = new AddVirtualFolderDto()
        {
            LibraryOptions = new LibraryOptions()
            {
                Enabled = false
            }
        };

        using HttpResponseMessage response = await client.PostAsJsonAsync("Library/VirtualFolders?name=test&refreshLibrary=true", body, _jsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    [Priority(-2)]
    public async Task UpdateLibraryOptions_Invalid_NotFound()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        UpdateLibraryOptionsDto body = new UpdateLibraryOptionsDto()
        {
            Id = Guid.NewGuid(),
            LibraryOptions = new LibraryOptions()
        };

        using HttpResponseMessage response = await client.PostAsJsonAsync("Library/VirtualFolders/LibraryOptions", body, _jsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    [Priority(-2)]
    public async Task UpdateLibraryOptions_Valid_Success()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        AddVirtualFolderDto createBody = new AddVirtualFolderDto()
        {
            LibraryOptions = new LibraryOptions()
            {
                Enabled = false
            }
        };

        using HttpResponseMessage createResponse = await client.PostAsJsonAsync("Library/VirtualFolders?name=test&refreshLibrary=true", createBody, _jsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, createResponse.StatusCode);

        await Task.Delay(2000).ConfigureAwait(true);

        using HttpResponseMessage response = await client.GetAsync("Library/VirtualFolders");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        VirtualFolderInfo? library = await response.Content.ReadFromJsonAsAsyncEnumerable<VirtualFolderInfo>(_jsonOptions)
            .FirstOrDefaultAsync(x => string.Equals(x?.Name, "test", StringComparison.Ordinal));
        Assert.NotNull(library);

        LibraryOptions options = library.LibraryOptions;
        Assert.NotNull(options);
        Assert.False(options.Enabled);
        options.Enabled = true;

        UpdateLibraryOptionsDto body = new UpdateLibraryOptionsDto()
        {
            Id = Guid.Parse(library.ItemId),
            LibraryOptions = options
        };

        using HttpResponseMessage response2 = await client.PostAsJsonAsync("Library/VirtualFolders/LibraryOptions", body, _jsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, response2.StatusCode);
    }

    [Fact]
    [Priority(1)]
    public async Task DeleteLibrary_Invalid_NotFound()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using HttpResponseMessage response = await client.DeleteAsync("Library/VirtualFolders?name=doesntExist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    [Priority(1)]
    public async Task DeleteLibrary_Valid_Success()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using HttpResponseMessage response = await client.DeleteAsync("Library/VirtualFolders?name=test&refreshLibrary=true");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
