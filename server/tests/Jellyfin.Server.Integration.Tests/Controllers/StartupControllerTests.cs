using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Api.Models.StartupDtos;
using Jellyfin.Extensions.Json;
using Xunit;
using Xunit.Priority;

namespace Jellyfin.Server.Integration.Tests.Controllers
{
    [TestCaseOrderer(PriorityOrderer.Name, PriorityOrderer.Assembly)]
    public sealed class StartupControllerTests : IClassFixture<JellyfinApplicationFactory>
    {
        private readonly JellyfinApplicationFactory _factory;
        private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Options;

        public StartupControllerTests(JellyfinApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        [Priority(-2)]
        public async Task Configuration_EditConfig_Success()
        {
            HttpClient client = _factory.CreateClient();

            StartupConfigurationDto config = new StartupConfigurationDto()
            {
                ServerName = "NewServer",
                UICulture = "NewCulture",
                MetadataCountryCode = "be",
                PreferredMetadataLanguage = "nl"
            };

            using HttpResponseMessage postResponse = await client.PostAsJsonAsync("/Startup/Configuration", config, _jsonOptions);
            Assert.Equal(HttpStatusCode.NoContent, postResponse.StatusCode);

            using HttpResponseMessage getResponse = await client.GetAsync("/Startup/Configuration");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            Assert.Equal(MediaTypeNames.Application.Json, getResponse.Content.Headers.ContentType?.MediaType);

            StartupConfigurationDto? newConfig = await getResponse.Content.ReadFromJsonAsync<StartupConfigurationDto>(_jsonOptions);
            Assert.Equal(config.ServerName, newConfig!.ServerName);
            Assert.Equal(config.UICulture, newConfig.UICulture);
            Assert.Equal(config.MetadataCountryCode, newConfig.MetadataCountryCode);
            Assert.Equal(config.PreferredMetadataLanguage, newConfig.PreferredMetadataLanguage);
        }

        [Fact]
        [Priority(-2)]
        public async Task User_DefaultUser_NameWithoutPassword()
        {
            HttpClient client = _factory.CreateClient();

            using HttpResponseMessage response = await client.GetAsync("/Startup/User");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);

            StartupUserDto? user = await response.Content.ReadFromJsonAsync<StartupUserDto>(_jsonOptions);
            Assert.NotNull(user);
            Assert.NotNull(user.Name);
            Assert.NotEmpty(user.Name);
            Assert.Null(user.Password);
        }

        [Fact]
        [Priority(-1)]
        public async Task User_EditUser_Success()
        {
            HttpClient client = _factory.CreateClient();

            StartupUserDto user = new StartupUserDto()
            {
                Name = "NewName",
                Password = "NewPassword"
            };

            HttpResponseMessage postResponse = await client.PostAsJsonAsync("/Startup/User", user, _jsonOptions);
            Assert.Equal(HttpStatusCode.NoContent, postResponse.StatusCode);

            HttpResponseMessage getResponse = await client.GetAsync("/Startup/User");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            Assert.Equal(MediaTypeNames.Application.Json, getResponse.Content.Headers.ContentType?.MediaType);

            StartupUserDto? newUser = await getResponse.Content.ReadFromJsonAsync<StartupUserDto>(_jsonOptions);
            Assert.NotNull(newUser);
            Assert.Equal(user.Name, newUser.Name);
            Assert.Null(newUser.Password);
        }

        [Fact]
        [Priority(0)]
        public async Task CompleteWizard_Success()
        {
            HttpClient client = _factory.CreateClient();

            HttpResponseMessage response = await client.PostAsync("/Startup/Complete", new ByteArrayContent(Array.Empty<byte>()));
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        [Priority(1)]
        public async Task GetFirstUser_CompleteWizard_Unauthorized()
        {
            HttpClient client = _factory.CreateClient();

            using HttpResponseMessage response = await client.GetAsync("/Startup/User");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
