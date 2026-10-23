using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BitFaster.Caching;
using Emby.Server.Implementations.Localization;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Localization
{
    public class LocalizationManagerTests
    {
        [Fact]
        public void GetCountries_All_Success()
        {
            LocalizationManager localizationManager = Setup(new ServerConfiguration
            {
                UICulture = "de-DE"
            });
            List<CountryInfo> countries = localizationManager.GetCountries().ToList();

            Assert.Equal(139, countries.Count);

            CountryInfo? germany = countries.FirstOrDefault(x => x.Name.Equals("DE", StringComparison.Ordinal));
            Assert.NotNull(germany);
            Assert.Equal("Germany", germany!.DisplayName);
            Assert.Equal("DEU", germany.ThreeLetterISORegionName);
            Assert.Equal("DE", germany.TwoLetterISORegionName);
        }

        [Fact]
        public async Task GetCultures_All_Success()
        {
            LocalizationManager localizationManager = Setup(new ServerConfiguration
            {
                UICulture = "de-DE"
            });
            await localizationManager.LoadAll();
            List<CultureDto> cultures = localizationManager.GetCultures().ToList();

            Assert.Equal(194, cultures.Count);

            CultureDto? germany = cultures.FirstOrDefault(x => x.TwoLetterISOLanguageName.Equals("de", StringComparison.Ordinal));
            Assert.NotNull(germany);
            Assert.Equal("deu", germany!.ThreeLetterISOLanguageName);
            Assert.Equal("German", germany.DisplayName);
            Assert.Equal("German", germany.Name);
            Assert.Contains("deu", germany.ThreeLetterISOLanguageNames);
            Assert.Contains("ger", germany.ThreeLetterISOLanguageNames);
        }

        [Fact]
        public async Task TryGetISO6392TFromB_Success()
        {
            LocalizationManager localizationManager = Setup(new ServerConfiguration
            {
                UICulture = "de-DE"
            });
            await localizationManager.LoadAll();

            string? isoT;

            // Translation ger -> deu
            Assert.True(localizationManager.TryGetISO6392TFromB("ger", out isoT));
            Assert.Equal("deu", isoT);

            // chi -> zho
            Assert.True(localizationManager.TryGetISO6392TFromB("chi", out isoT));
            Assert.Equal("zho", isoT);

            // eng is already ISO 639-2/T
            Assert.False(localizationManager.TryGetISO6392TFromB("eng", out isoT));
            Assert.Null(isoT);
        }

        [Theory]
        [InlineData("de")]
        [InlineData("deu")]
        [InlineData("ger")]
        [InlineData("german")]
        public async Task FindLanguageInfo_Valid_Success(string identifier)
        {
            LocalizationManager localizationManager = Setup(new ServerConfiguration
            {
                UICulture = "de-DE"
            });
            await localizationManager.LoadAll();

            CultureDto? germany = localizationManager.FindLanguageInfo(identifier);
            Assert.NotNull(germany);

            Assert.Equal("deu", germany!.ThreeLetterISOLanguageName);
            Assert.Equal("German", germany.DisplayName);
            Assert.Equal("German", germany.Name);
            Assert.Contains("deu", germany.ThreeLetterISOLanguageNames);
            Assert.Contains("ger", germany.ThreeLetterISOLanguageNames);
        }

        [Fact]
        public async Task GetParentalRatings_Default_Success()
        {
            LocalizationManager localizationManager = Setup(new ServerConfiguration
            {
                UICulture = "de-DE"
            });
            await localizationManager.LoadAll();
            List<ParentalRating> ratings = localizationManager.GetParentalRatings().ToList();

            Assert.Equal(56, ratings.Count);

            ParentalRating? tvma = ratings.FirstOrDefault(x => x.Name.Equals("TV-MA", StringComparison.Ordinal));
            Assert.NotNull(tvma);
            Assert.Equal(17, tvma!.RatingScore!.Score);
        }

        [Fact]
        public async Task GetParentalRatings_ConfiguredCountryCode_Success()
        {
            LocalizationManager localizationManager = Setup(new ServerConfiguration()
            {
                MetadataCountryCode = "DE"
            });
            await localizationManager.LoadAll();
            List<ParentalRating> ratings = localizationManager.GetParentalRatings().ToList();

            Assert.Equal(24, ratings.Count);

            ParentalRating? fsk = ratings.FirstOrDefault(x => x.Name.Equals("FSK-12", StringComparison.Ordinal));
            Assert.NotNull(fsk);
            Assert.Equal(12, fsk!.RatingScore!.Score);
        }

        [Theory]
        [InlineData("CA-R", "CA", 18, 1)]
        [InlineData("FSK-16", "DE", 16, null)]
        [InlineData("FSK-18", "DE", 18, null)]
        [InlineData("FSK-18", "US", 18, null)]
        [InlineData("TV-MA", "US", 17, 1)]
        [InlineData("XXX", "asdf", 1000, null)]
        [InlineData("Germany: FSK-18", "DE", 18, null)]
        [InlineData("Rated : R", "US", 17, 0)]
        [InlineData("Rated: R", "US", 17, 0)]
        [InlineData("Rated R", "US", 17, 0)]
        [InlineData(" PG-13 ", "US", 13, 0)]
        public async Task GetRatingLevel_GivenValidString_Success(string value, string countryCode, int? expectedScore, int? expectedSubScore)
        {
            LocalizationManager localizationManager = Setup(new ServerConfiguration()
            {
                MetadataCountryCode = countryCode
            });
            await localizationManager.LoadAll();
            ParentalRatingScore? score = localizationManager.GetRatingScore(value);
            Assert.NotNull(score);
            Assert.Equal(expectedScore, score.Score);
            Assert.Equal(expectedSubScore, score.SubScore);
        }

        [Theory]
        [InlineData("0", 0, null)]
        [InlineData("1", 1, null)]
        [InlineData("6", 6, null)]
        [InlineData("12", 12, null)]
        [InlineData("42", 42, null)]
        [InlineData("9999", 9999, null)]
        public async Task GetRatingLevel_GivenValidAge_Success(string value, int? expectedScore, int? expectedSubScore)
        {
            LocalizationManager localizationManager = Setup(new ServerConfiguration { MetadataCountryCode = "nl" });
            await localizationManager.LoadAll();
            ParentalRatingScore? score = localizationManager.GetRatingScore(value);
            Assert.NotNull(score);
            Assert.Equal(expectedScore, score.Score);
            Assert.Equal(expectedSubScore, score.SubScore);
        }

        [Fact]
        public async Task GetRatingLevel_GivenUnratedString_Success()
        {
            LocalizationManager localizationManager = Setup(new ServerConfiguration()
            {
                UICulture = "de-DE"
            });
            await localizationManager.LoadAll();
            Assert.Null(localizationManager.GetRatingScore("NR"));
            Assert.Null(localizationManager.GetRatingScore("unrated"));
            Assert.Null(localizationManager.GetRatingScore("Not Rated"));
            Assert.Null(localizationManager.GetRatingScore("n/a"));
        }

        [Theory]
        [InlineData("-NO RATING SHOWN-")]
        [InlineData(":NO RATING SHOWN:")]
        public async Task GetRatingLevel_Split_Success(string value)
        {
            LocalizationManager localizationManager = Setup(new ServerConfiguration()
            {
                UICulture = "en-US"
            });
            await localizationManager.LoadAll();

            Assert.Null(localizationManager.GetRatingScore(value));
        }

        [Theory]
        [InlineData("TV-MA", "DE", 17, 1)] // US-only rating, DE country code
        [InlineData("PG-13", "FR", 13, 0)] // US-only rating, FR country code
        [InlineData("R", "JP", 17, 0)] // US-only rating, JP country code
        public async Task GetRatingScore_FallbackPrioritizesUS_Success(string rating, string countryCode, int expectedScore, int? expectedSubScore)
        {
            LocalizationManager localizationManager = Setup(new ServerConfiguration()
            {
                MetadataCountryCode = countryCode
            });
            await localizationManager.LoadAll();

            ParentalRatingScore? score = localizationManager.GetRatingScore(rating);

            Assert.NotNull(score);
            Assert.Equal(expectedScore, score.Score);
            Assert.Equal(expectedSubScore, score.SubScore);
        }

        [Theory]
        [InlineData("Default", "Default")]
        [InlineData("HeaderLiveTV", "Live TV")]
        public void GetLocalizedString_Valid_Success(string key, string expected)
        {
            LocalizationManager localizationManager = Setup(new ServerConfiguration()
            {
                UICulture = "en-US"
            });

            string translated = localizationManager.GetLocalizedString(key);
            Assert.NotNull(translated);
            Assert.Equal(expected, translated);
        }

        [Fact]
        public void GetLocalizedString_Invalid_Success()
        {
            LocalizationManager localizationManager = Setup(new ServerConfiguration()
            {
                UICulture = "en-US"
            });

            string key = "SuperInvalidTranslationKeyThatWillNeverBeAdded";

            string translated = localizationManager.GetLocalizedString(key);
            Assert.NotNull(translated);
            Assert.Equal(key, translated);
        }

        private LocalizationManager Setup(ServerConfiguration config)
        {
            Mock<IServerConfigurationManager> mockConfiguration = new Mock<IServerConfigurationManager>();
            mockConfiguration.SetupGet(x => x.Configuration).Returns(config);

            return new LocalizationManager(mockConfiguration.Object, new NullLogger<LocalizationManager>());
        }
    }
}
