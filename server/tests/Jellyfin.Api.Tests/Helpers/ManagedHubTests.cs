using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Jellyfin.Api.Controllers;
using Jellyfin.Api.Helpers;
using Jellyfin.Api.Models.VisularrDtos;
using MediaBrowser.Common.Api;
using MediaBrowser.Common.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Helpers;

public sealed class ManagedHubTests : IDisposable
{
    private readonly string _configDir = Path.Combine(Path.GetTempPath(), "visularr-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_configDir))
        {
            Directory.Delete(_configDir, true);
        }
    }

    [Theory]
    [InlineData("http://localhost:8080", "http://localhost:8080")]
    [InlineData("https://hub.example.com/base/", "https://hub.example.com/base")]
    [InlineData(" http://10.0.0.2:5000 ", "http://10.0.0.2:5000")]
    [InlineData("ftp://hub", null)]
    [InlineData("file:///etc/passwd", null)]
    [InlineData("hub:8080", null)]
    [InlineData("/relative", null)]
    [InlineData("http://user:pass@hub", null)]
    [InlineData("http://hub/?apikey=x", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeUrl_AcceptsOnlyAbsoluteHttpUrls(string? url, string? expected)
        => Assert.Equal(expected, ManagedHub.NormalizeUrl(url));

    [Fact]
    public void Get_NothingStored_IsEmpty()
    {
        ManagedDto managed = CreateHub().Get();
        Assert.Null(managed.Name);
        Assert.Null(managed.Url);
    }

    [Theory]
    [InlineData("linkarr", "ftp://hub")]
    [InlineData("linkarr", "javascript:alert(1)")]
    [InlineData("linkarr", null)]
    [InlineData(" ", "https://hub.example.com")]
    [InlineData(null, "https://hub.example.com")]
    public void TrySave_Invalid_StoresNothing(string? name, string? url)
    {
        ManagedHub hub = CreateHub();
        Assert.False(hub.TrySave(name, url));
        Assert.False(hub.TrySave(new string('a', 101), "https://hub.example.com"));
        Assert.Null(hub.Get().Url);
    }

    [Fact]
    public void TrySave_WritesAtomically_AndClearForgets()
    {
        ManagedHub hub = CreateHub();
        Assert.True(hub.TrySave(" linkarr ", "https://hub.example.com/"));
        Assert.Equal(new[] { "visularr-managed.json" }, Directory.GetFiles(_configDir).Select(Path.GetFileName));

        ManagedDto managed = CreateHub().Get();
        Assert.Equal("linkarr", managed.Name);
        Assert.Equal("https://hub.example.com", managed.Url);

        hub.Clear();
        hub.Clear();
        Assert.Null(hub.Get().Name);
        Assert.Empty(Directory.GetFiles(_configDir));
    }

    [Fact]
    public void CorruptFile_IsEmpty()
    {
        Directory.CreateDirectory(_configDir);
        File.WriteAllText(Path.Combine(_configDir, "visularr-managed.json"), "{ not json");
        Assert.Null(CreateHub().Get().Url);
    }

    [Theory]
    [InlineData(nameof(VisularrManagedController.Set))]
    [InlineData(nameof(VisularrManagedController.Clear))]
    public void Writes_RequireElevation(string action)
    {
        MethodInfo method = typeof(VisularrManagedController).GetMethod(action)!;
        Assert.Equal(Policies.RequiresElevation, method.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
    }

    [Fact]
    public void Read_NeedsOnlyASignedInUser()
    {
        Assert.NotNull(typeof(VisularrManagedController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(typeof(VisularrManagedController).GetMethod(nameof(VisularrManagedController.Get))!.GetCustomAttribute<AuthorizeAttribute>());
    }

    private ManagedHub CreateHub()
    {
        Mock<IApplicationPaths> paths = new Mock<IApplicationPaths>();
        paths.SetupGet(p => p.ConfigurationDirectoryPath).Returns(_configDir);
        return new ManagedHub(paths.Object, new Mock<ILogger<ManagedHub>>().Object);
    }
}
