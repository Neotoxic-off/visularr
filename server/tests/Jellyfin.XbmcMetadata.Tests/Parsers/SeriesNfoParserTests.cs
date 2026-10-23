using System;
using System.Linq;
using System.Threading;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using MediaBrowser.XbmcMetadata.Parsers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.XbmcMetadata.Tests.Parsers
{
    public class SeriesNfoParserTests
    {
        private readonly SeriesNfoParser _parser;

        public SeriesNfoParserTests()
        {
            Mock<IProviderManager> providerManager = new Mock<IProviderManager>();
            providerManager.Setup(x => x.GetExternalIdInfos(It.IsAny<IHasProviderIds>()))
                .Returns(Enumerable.Empty<ExternalIdInfo>());
            Mock<IConfigurationManager> config = new Mock<IConfigurationManager>();
            config.Setup(x => x.GetConfiguration(It.IsAny<string>()))
                .Returns(new XbmcMetadataOptions());
            Mock<IUserManager> user = new Mock<IUserManager>();
            Mock<IUserDataManager> userData = new Mock<IUserDataManager>();
            Mock<IDirectoryService> directoryService = new Mock<IDirectoryService>();

            _parser = new SeriesNfoParser(new NullLogger<SeriesNfoParser>(), config.Object, providerManager.Object, user.Object, userData.Object, directoryService.Object);
        }

        [Fact]
        public void Fetch_Valid_Success()
        {
            MetadataResult<Series> result = new MetadataResult<Series>()
            {
                Item = new Series()
            };

            _parser.Fetch(result, "Test Data/American Gods.nfo", CancellationToken.None);
            Series item = result.Item;

            Assert.Equal("American Gods", item.OriginalTitle);
            Assert.Equal(string.Empty, item.Tagline);
            Assert.Equal(0, item.RunTimeTicks);
            Assert.Equal("46639", item.ProviderIds[MetadataProvider.Tmdb.ToString()]);
            Assert.Equal("253573", item.ProviderIds[MetadataProvider.Tvdb.ToString()]);
            Assert.Equal("tt11111", item.ProviderIds[MetadataProvider.Imdb.ToString()]);

            Assert.Equal(3, item.Genres.Length);
            Assert.Contains("Drama", item.Genres);
            Assert.Contains("Mystery", item.Genres);
            Assert.Contains("Sci-Fi & Fantasy", item.Genres);

            Assert.Equal(new DateTime(2017, 4, 30), item.PremiereDate);
            Assert.Single(item.Studios);
            Assert.Contains("Starz", item.Studios);
            Assert.Equal("9 PM", item.AirTime);
            Assert.Single(item.AirDays);
            Assert.Contains(DayOfWeek.Friday, item.AirDays);
            Assert.Equal(SeriesStatus.Ended, item.Status);

            Assert.Equal(6, result.People.Count);

            Assert.True(result.People.All(x => x.Type == PersonKind.Actor));

            // Only test one actor
            PersonInfo? sweeney = result.People.FirstOrDefault(x => x.Role.Equals("Mad Sweeney", StringComparison.Ordinal));
            Assert.NotNull(sweeney);
            Assert.Equal("Pablo Schreiber", sweeney!.Name);
            Assert.Equal(3, sweeney!.SortOrder);
            Assert.Equal("http://image.tmdb.org/t/p/original/uo8YljeePz3pbj7gvWXdB4gOOW4.jpg", sweeney!.ImageUrl);

            Assert.Equal(new DateTime(2017, 10, 7, 14, 25, 47), item.DateCreated);
        }

        [Theory]
        [InlineData("Test Data/Tvdb.nfo", "Tvdb", "121361")]
        public void Parse_UrlFile_Success(string path, string provider, string id)
        {
            MetadataResult<Series> result = new MetadataResult<Series>()
            {
                Item = new Series()
            };

            _parser.Fetch(result, path, CancellationToken.None);
            Series item = result.Item;

            Assert.Equal(id, item.ProviderIds[provider]);
        }

        [Fact]
        public void Fetch_WithNullItem_ThrowsArgumentException()
        {
            MetadataResult<Series> result = new MetadataResult<Series>();

            Assert.Throws<ArgumentException>(() => _parser.Fetch(result, "Test Data/American Gods.nfo", CancellationToken.None));
        }

        [Fact]
        public void Fetch_NullResult_ThrowsArgumentException()
        {
            MetadataResult<Series> result = new MetadataResult<Series>()
            {
                Item = new Series()
            };

            Assert.Throws<ArgumentException>(() => _parser.Fetch(result, string.Empty, CancellationToken.None));
        }
    }
}
