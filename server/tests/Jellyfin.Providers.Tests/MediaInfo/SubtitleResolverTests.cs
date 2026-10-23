using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Emby.Naming.Common;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Providers.MediaInfo;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Providers.Tests.MediaInfo;

public class SubtitleResolverTests
{
    private readonly SubtitleResolver _subtitleResolver;

    public SubtitleResolverTests()
    {
        // prep BaseItem and Video for calls made that expect managers
        Video.RecordingsManager = Mock.Of<IRecordingsManager>();

        IServerApplicationPaths applicationPaths = new Mock<IServerApplicationPaths>().Object;
        Mock<IServerConfigurationManager> serverConfig = new Mock<IServerConfigurationManager>();
        serverConfig.Setup(c => c.ApplicationPaths)
            .Returns(applicationPaths);
        BaseItem.ConfigurationManager = serverConfig.Object;

        // build resolver to test with
        ILocalizationManager localizationManager = Mock.Of<ILocalizationManager>();

        Mock<IMediaEncoder> mediaEncoder = new Mock<IMediaEncoder>(MockBehavior.Strict);
        mediaEncoder.Setup(me => me.GetMediaInfo(It.IsAny<MediaInfoRequest>(), It.IsAny<CancellationToken>()))
            .Returns<MediaInfoRequest, CancellationToken>((_, _) => Task.FromResult(new MediaBrowser.Model.MediaInfo.MediaInfo
            {
                MediaStreams = new List<MediaStream>
                {
                    new()
                    {
                        Type = MediaStreamType.Subtitle
                    }
                }
            }));

        Mock<IFileSystem> fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        fileSystem.Setup(fs => fs.DirectoryExists(It.IsRegex(MediaInfoResolverTests.VideoDirectoryRegex)))
            .Returns(true);
        fileSystem.Setup(fs => fs.DirectoryExists(It.IsRegex(MediaInfoResolverTests.MetadataDirectoryRegex)))
            .Returns(true);

        _subtitleResolver = new SubtitleResolver(Mock.Of<ILogger<SubtitleResolver>>(), localizationManager, mediaEncoder.Object, fileSystem.Object, new NamingOptions());
    }

    [Theory]
    [InlineData("My.Video.srt", false, true)]
    [InlineData("My.Video.mp3", false, false)]
    [InlineData("My.Video.srt", true, true)]
    [InlineData("My.Video.mp3", true, false)]
    public async Task GetExternalStreams_MixedFilenames_PicksSubtitles(string file, bool metadataDirectory, bool matches)
    {
        BaseItem.MediaSourceManager = Mock.Of<IMediaSourceManager>();

        Movie video = new Movie
        {
            Path = MediaInfoResolverTests.VideoDirectoryPath + "/My.Video.mkv"
        };

        IDirectoryService directoryService = MediaInfoResolverTests.GetDirectoryServiceForExternalFile(file, metadataDirectory);
        IReadOnlyList<MediaStream> streams = await _subtitleResolver.GetExternalStreamsAsync(video, 0, directoryService, false, CancellationToken.None);

        if (matches)
        {
            Assert.Single(streams);
            MediaStream actual = streams[0];
            Assert.Equal(MediaStreamType.Subtitle, actual.Type);
        }
        else
        {
            Assert.Empty(streams);
        }
    }
}
