using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Server.Helpers;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Security;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Server.Integration.Tests
{
    public sealed class VisularrBootstrapTests : IClassFixture<JellyfinApplicationFactory>
    {
        private const string ApiKey = "0123456789abcdefABCDEF0123456789visularr";

        private readonly JellyfinApplicationFactory _factory;

        public VisularrBootstrapTests(JellyfinApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task RunAsync_FreshServer_ProvisionsKeyWizardLibrariesAndLocksBootstrapUser()
        {
            IServiceProvider services = _factory.Services;
            string mediaRoot = Path.Combine(Path.GetTempPath(), "visularr-media-" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(mediaRoot, "movies"));
            Directory.CreateDirectory(Path.Combine(mediaRoot, "anime"));
            try
            {
                VisularrBootstrap bootstrap = new VisularrBootstrap(services, NullLogger.Instance, ApiKey, "fr", "FR", mediaRoot);

                Assert.True(await bootstrap.RunAsync());
                Assert.False(await bootstrap.RunAsync());

                IReadOnlyList<AuthenticationInfo> keys = await services.GetRequiredService<IAuthenticationManager>().GetApiKeys();
                AuthenticationInfo key = Assert.Single(keys, k => k.AccessToken == ApiKey);
                Assert.Equal(VisularrBootstrap.ApiKeyAppName, key.AppName);

                IServerConfigurationManager config = services.GetRequiredService<IServerConfigurationManager>();
                Assert.True(config.Configuration.IsStartupWizardCompleted);
                Assert.Equal("en-US", config.Configuration.UICulture);
                Assert.Equal("fr", config.Configuration.PreferredMetadataLanguage);
                Assert.Equal("FR", config.Configuration.MetadataCountryCode);

                List<VirtualFolderInfo> folders = services.GetRequiredService<ILibraryManager>().GetVirtualFolders();
                VirtualFolderInfo movies = Assert.Single(folders, f => f.Name == "Movies");
                Assert.Equal(CollectionTypeOptions.movies, movies.CollectionType);
                Assert.Equal(Path.Combine(mediaRoot, "movies"), Assert.Single(movies.Locations));
                Assert.False(movies.LibraryOptions.ExtractTrickplayImagesDuringLibraryScan);
                Assert.False(movies.LibraryOptions.ExtractChapterImagesDuringLibraryScan);
                Assert.True(movies.LibraryOptions.EnableRealtimeMonitor);
                Assert.Equal(CollectionTypeOptions.tvshows, Assert.Single(folders, f => f.Name == "Anime").CollectionType);
                Assert.DoesNotContain(folders, f => f.Name == "Shows"); // its folder does not exist

                IUserManager userManager = services.GetRequiredService<IUserManager>();
                User user = Assert.Single(userManager.GetUsers());
                Assert.Equal(VisularrBootstrap.BootstrapUserName, user.Username);
                Assert.True(user.HasPermission(PermissionKind.IsHidden));
                Assert.Null(await userManager.AuthenticateUser(VisularrBootstrap.BootstrapUserName, string.Empty, "127.0.0.1", true));

                // The anonymous first-run endpoints are closed and the key is accepted as an admin token.
                HttpClient client = _factory.CreateClient();
                using HttpResponseMessage anonymous = await client.GetAsync("/Startup/User");
                Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
                client.DefaultRequestHeaders.Add("X-Emby-Token", ApiKey);
                using HttpResponseMessage users = await client.GetAsync("/Users");
                Assert.Equal(HttpStatusCode.OK, users.StatusCode);
            }
            finally
            {
                Directory.Delete(mediaRoot, true);
            }
        }

        [Fact]
        public void ReadApiKey_Unset_ReturnsNullSoNothingRuns()
        {
            string? previous = Environment.GetEnvironmentVariable("VISULARR_API_KEY");
            try
            {
                Environment.SetEnvironmentVariable("VISULARR_API_KEY", null);
                Assert.Null(VisularrBootstrap.ReadApiKey());

                Environment.SetEnvironmentVariable("VISULARR_API_KEY", ApiKey);
                Assert.Equal(ApiKey, VisularrBootstrap.ReadApiKey());

                Environment.SetEnvironmentVariable("VISULARR_API_KEY", "too-short");
                Assert.Throws<InvalidOperationException>(() => VisularrBootstrap.ReadApiKey());
            }
            finally
            {
                Environment.SetEnvironmentVariable("VISULARR_API_KEY", previous);
            }
        }

        [Theory]
        [InlineData("0123456789abcdef0123456789abcdef", true)]
        [InlineData("0123456789abcdef0123456789abcde", false)]
        [InlineData("0123456789abcdef0123456789abcde-", false)]
        [InlineData("0123456789abcdef0123456789abcdé1", false)]
        public void IsValidApiKey_ChecksLengthAndAlphabet(string key, bool expected)
        {
            Assert.Equal(expected, VisularrBootstrap.IsValidApiKey(key));
        }
    }
}
