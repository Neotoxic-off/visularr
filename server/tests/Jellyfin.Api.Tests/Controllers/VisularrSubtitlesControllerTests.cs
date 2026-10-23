using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Controllers;
using Jellyfin.Api.Helpers;
using Jellyfin.Api.Models.VisularrDtos;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Server.Implementations.Users;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.MediaInfo;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Controllers;

public sealed class VisularrSubtitlesControllerTests : IDisposable
{
    private const string Srt = "1\n00:00:01,000 --> 00:00:02,000\nHello\n";
    private const string SourceId = "0123456789abcdef0123456789abcdef";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "visularr-sync-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _tempRoot;
    private readonly string _mediaDir;
    private readonly string _videoPath;
    private readonly User _user;
    private readonly Video _video;
    private readonly Mock<ILibraryManager> _library = new();
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<IMediaSourceManager> _sources = new();
    private readonly Mock<ISubtitleEncoder> _encoder = new();
    private readonly Mock<ISubtitleManager> _subtitles = new();
    private readonly Mock<IProviderManager> _providers = new();
    private readonly Mock<IMediaEncoder> _mediaEncoder = new();
    private readonly List<MediaStream> _streams;

    public VisularrSubtitlesControllerTests()
    {
        _tempRoot = Path.Combine(_root, "transcodes", "subtitle-sync");
        _mediaDir = Path.Combine(_root, "media");
        Directory.CreateDirectory(_mediaDir);
        _videoPath = Path.Combine(_mediaDir, "Movie (2020).mkv");
        File.WriteAllText(_videoPath, "not really a video");
        // The media mount is read-only in the container.
        File.SetAttributes(_videoPath, FileAttributes.ReadOnly);

        _user = new User("viewer", typeof(DefaultAuthenticationProvider).FullName!, typeof(DefaultPasswordResetProvider).FullName!);
        _user.AddDefaultPermissions();
        _video = new Video { Id = Guid.NewGuid(), Path = _videoPath };
        _streams =
        [
            new MediaStream { Type = MediaStreamType.Video, Index = 0 },
            new MediaStream { Type = MediaStreamType.Subtitle, Index = 2, Codec = "subrip", Language = "eng" },
            new MediaStream { Type = MediaStreamType.Subtitle, Index = 3, Codec = "hdmv_pgs_subtitle", Language = "eng" },
        ];

        _users.Setup(u => u.GetUserById(It.IsAny<Guid>())).Returns(_user);
        _library.Setup(l => l.GetItemById<Video>(_video.Id, It.IsAny<User?>())).Returns(_video);
        _sources.Setup(s => s.GetStaticMediaSources(_video, false, It.IsAny<User>()))
            .Returns([new MediaSourceInfo { Id = SourceId, Path = _videoPath, Protocol = MediaProtocol.File, MediaStreams = _streams }]);
        _sources.SetupSequence(s => s.GetMediaStreams(_video.Id))
            .Returns(_streams)
            .Returns([.. _streams, new MediaStream { Type = MediaStreamType.Subtitle, Index = 4, IsExternal = true, Path = "/config/metadata/Movie.eng.eng (synced).srt" }]);
        _encoder.Setup(e => e.GetSubtitles(_video, SourceId, 2, "srt", 0, 0, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(Encoding.UTF8.GetBytes(Srt)));
        _providers.Setup(p => p.RefreshSingleItem(_video, It.IsAny<MetadataRefreshOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ItemUpdateType.MetadataImport);
        _mediaEncoder.SetupGet(e => e.EncoderPath).Returns("/usr/lib/jellyfin-ffmpeg/ffmpeg");
        _mediaEncoder.SetupGet(e => e.ProbePath).Returns("/usr/lib/jellyfin-ffmpeg/ffprobe");
    }

    public void Dispose()
    {
        if (File.Exists(_videoPath))
        {
            File.SetAttributes(_videoPath, FileAttributes.Normal);
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void BuildArguments_PassesPathsVerbatim()
    {
        // No shell: quotes, spaces and separators stay inside one argument each.
        string video = "/media/A; rm -rf \"x\" $(id).mkv";
        Assert.Equal(new[] { video, "/tmp/in.srt", "/tmp/out.srt" }, SubtitleAligner.BuildArguments(video, "/tmp/in.srt", "/tmp/out.srt"));
    }

    [Theory]
    [InlineData("eng", "eng.eng (synced)")]
    [InlineData("pt-BR", "pt-br.pt-br (synced)")]
    [InlineData("und", "synced")]
    [InlineData(null, "synced")]
    [InlineData("../etc", "synced")]
    [InlineData("en/x", "synced")]
    public void SyncedLanguage_NamesTheTitle(string? language, string expected)
        => Assert.Equal(expected, VisularrSubtitlesController.SyncedLanguage(language));

    [Fact]
    public async Task Align_FakeAligner_ReturnsOutputAndCleansUp()
    {
        SubtitleAligner aligner = CreateAligner(Script("copy"), TimeSpan.FromSeconds(30));
        byte[] result = await aligner.AlignAsync(_videoPath, new MemoryStream(Encoding.UTF8.GetBytes(Srt)), CancellationToken.None);
        Assert.Equal(Srt, Encoding.UTF8.GetString(result));
        AssertTempEmpty();
    }

    [Fact]
    public async Task Align_Timeout_KillsAndCleansUp()
    {
        SubtitleAligner aligner = CreateAligner(Script("sleep"), TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<TimeoutException>(() => aligner.AlignAsync(_videoPath, new MemoryStream(Encoding.UTF8.GetBytes(Srt)), CancellationToken.None));
        AssertTempEmpty();
    }

    [Fact]
    public async Task Align_Failure_ReportsAndCleansUp()
    {
        SubtitleAligner aligner = CreateAligner(Script("fail"), TimeSpan.FromSeconds(30));
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => aligner.AlignAsync(_videoPath, new MemoryStream(Encoding.UTF8.GetBytes(Srt)), CancellationToken.None));
        Assert.Contains("boom", ex.Message, StringComparison.Ordinal);
        AssertTempEmpty();
    }

    [Fact]
    public async Task Sync_NoAligner_Returns501()
    {
        ObjectResult result = Assert.IsType<ObjectResult>((await CreateController(null).Sync(_video.Id, Body(2), CancellationToken.None)).Result);
        Assert.Equal(StatusCodes.Status501NotImplemented, result.StatusCode);
    }

    [Fact]
    public async Task Sync_ItemNotVisibleToUser_Returns404()
    {
        ActionResult<SubtitleSyncResultDto> result = await CreateController(Script("copy")).Sync(Guid.NewGuid(), Body(2), CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Sync_UserWithoutPlayback_IsForbidden()
    {
        _user.SetPermission(PermissionKind.EnableMediaPlayback, false);
        ActionResult<SubtitleSyncResultDto> result = await CreateController(Script("copy")).Sync(_video.Id, Body(2), CancellationToken.None);
        Assert.IsType<ForbidResult>(result.Result);
        _subtitles.Verify(s => s.UploadSubtitle(It.IsAny<Video>(), It.IsAny<SubtitleResponse>()), Times.Never);
    }

    [Fact]
    public async Task Sync_ImageSubtitle_Returns400()
    {
        ActionResult<SubtitleSyncResultDto> result = await CreateController(Script("copy")).Sync(_video.Id, Body(3), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Sync_AllSlotsBusy_Returns429()
    {
        VisularrSubtitlesController controller = CreateController(Script("copy"), out SubtitleAligner aligner);
        for (int i = 0; i < SubtitleAligner.MaxConcurrent; i++)
        {
            Assert.True(aligner.TryEnter());
        }

        ObjectResult result = Assert.IsType<ObjectResult>((await controller.Sync(_video.Id, Body(2), CancellationToken.None)).Result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, result.StatusCode);
    }

    [Fact]
    public async Task Sync_SavesThroughJellyfin_NeverTouchesMedia()
    {
        SubtitleResponse? saved = null;
        string? savedText = null;
        _subtitles.Setup(s => s.UploadSubtitle(_video, It.IsAny<SubtitleResponse>()))
            .Callback((Video _, SubtitleResponse r) =>
            {
                saved = r;
                savedText = new StreamReader(r.Stream).ReadToEnd();
            })
            .Returns(Task.CompletedTask);
        string[] mediaBefore = Directory.GetFileSystemEntries(_mediaDir);

        VisularrSubtitlesController controller = CreateController(Script("copy"), out SubtitleAligner aligner);
        ActionResult<SubtitleSyncResultDto> result = await controller.Sync(_video.Id, Body(2), CancellationToken.None);

        Assert.Equal(4, result.Value?.StreamIndex);
        Assert.NotNull(saved);
        Assert.Equal("srt", saved.Format);
        Assert.Equal("eng.eng (synced)", saved.Language);
        Assert.Equal(Srt, savedText);
        Assert.Equal(mediaBefore, Directory.GetFileSystemEntries(_mediaDir));
        AssertTempEmpty();
        // The slot is given back.
        for (int i = 0; i < SubtitleAligner.MaxConcurrent; i++)
        {
            Assert.True(aligner.TryEnter());
        }
    }

    private static SubtitleSyncDto Body(int index) => new SubtitleSyncDto { MediaSourceId = SourceId, StreamIndex = index };

    private void AssertTempEmpty()
        => Assert.True(!Directory.Exists(_tempRoot) || Directory.GetFileSystemEntries(_tempRoot).Length == 0);

    private SubtitleAligner CreateAligner(string? path, TimeSpan timeout)
        => new SubtitleAligner(path, () => _tempRoot, _mediaEncoder.Object, timeout, NullLogger<SubtitleAligner>.Instance);

    private VisularrSubtitlesController CreateController(string? alignerPath) => CreateController(alignerPath, out _);

    private VisularrSubtitlesController CreateController(string? alignerPath, out SubtitleAligner aligner)
    {
        aligner = CreateAligner(alignerPath, TimeSpan.FromSeconds(30));
        VisularrSubtitlesController controller = new VisularrSubtitlesController(
            _library.Object,
            _users.Object,
            _sources.Object,
            _encoder.Object,
            _subtitles.Object,
            _providers.Object,
            Mock.Of<IFileSystem>(),
            aligner,
            NullLogger<VisularrSubtitlesController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(InternalClaimTypes.UserId, _user.Id.ToString("N"))], "Test")),
            },
        };
        return controller;
    }

    // A stand-in for alass with the same argument shape: <video> <input> <output>.
    private string Script(string kind)
    {
        Directory.CreateDirectory(_root);
        bool windows = OperatingSystem.IsWindows();
        string body = (kind, windows) switch
        {
            ("copy", true) => "@copy /y \"%~2\" \"%~3\" >nul",
            ("sleep", true) => "@ping -n 60 127.0.0.1 >nul",
            ("fail", true) => "@echo boom 1>&2\r\n@exit /b 3",
            ("copy", false) => "#!/bin/sh\ncp \"$2\" \"$3\"",
            ("sleep", false) => "#!/bin/sh\nsleep 60",
            _ => "#!/bin/sh\necho boom >&2\nexit 3",
        };
        string path = Path.Combine(_root, $"fake-alass-{kind}{(windows ? ".cmd" : ".sh")}");
        File.WriteAllText(path, body + (windows ? "\r\n" : "\n"));
        if (!windows)
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }
}
