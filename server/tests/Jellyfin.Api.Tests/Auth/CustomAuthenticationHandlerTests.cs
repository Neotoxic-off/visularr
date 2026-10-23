using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoFixture;
using AutoFixture.AutoMoq;
using Jellyfin.Api.Auth;
using Jellyfin.Api.Constants;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Auth
{
    public class CustomAuthenticationHandlerTests
    {
        private readonly IFixture _fixture;

        private readonly Mock<IAuthService> _jellyfinAuthServiceMock;

        private readonly CustomAuthenticationHandler _sut;
        private readonly AuthenticationScheme _scheme;

        public CustomAuthenticationHandlerTests()
        {
            AutoMoqCustomization fixtureCustomizations = new AutoMoqCustomization
            {
                ConfigureMembers = true
            };

            _fixture = new Fixture().Customize(fixtureCustomizations);
            AllowFixtureCircularDependencies();

            _jellyfinAuthServiceMock = _fixture.Freeze<Mock<IAuthService>>();
            Mock<IOptionsMonitor<AuthenticationSchemeOptions>> optionsMonitorMock = _fixture.Freeze<Mock<IOptionsMonitor<AuthenticationSchemeOptions>>>();
            Mock<IServiceProvider> serviceProviderMock = _fixture.Freeze<Mock<IServiceProvider>>();
            Mock<IAuthenticationService> authenticationServiceMock = _fixture.Freeze<Mock<IAuthenticationService>>();
            _fixture.Register<ILoggerFactory>(() => new NullLoggerFactory());

            serviceProviderMock.Setup(s => s.GetService(typeof(IAuthenticationService)))
                .Returns(authenticationServiceMock.Object);

            optionsMonitorMock.Setup(o => o.Get(It.IsAny<string>()))
                .Returns(new AuthenticationSchemeOptions
                {
                    ForwardAuthenticate = null
                });

            HttpContext context = new DefaultHttpContext
            {
                RequestServices = serviceProviderMock.Object
            };

            _scheme = new AuthenticationScheme(
                _fixture.Create<string>(),
                null,
                typeof(CustomAuthenticationHandler));

            _sut = _fixture.Create<CustomAuthenticationHandler>();
            _sut.InitializeAsync(_scheme, context).Wait();
        }

        [Fact]
        public async Task HandleAuthenticateAsyncShouldProvideNoResultOnAuthenticationException()
        {
            string errorMessage = _fixture.Create<string>();

            _jellyfinAuthServiceMock.Setup(
                    a => a.Authenticate(
                        It.IsAny<HttpRequest>()))
                .Throws(new AuthenticationException(errorMessage));

            AuthenticateResult authenticateResult = await _sut.AuthenticateAsync();

            Assert.False(authenticateResult.Succeeded);
            Assert.True(authenticateResult.None);
        }

        [Fact]
        public async Task HandleAuthenticateAsyncShouldSucceedWithUser()
        {
            SetupUser();
            AuthenticateResult authenticateResult = await _sut.AuthenticateAsync();

            Assert.True(authenticateResult.Succeeded);
            Assert.Null(authenticateResult.Failure);
        }

        [Fact]
        public async Task HandleAuthenticateAsyncShouldAssignNameClaim()
        {
            AuthorizationInfo authorizationInfo = SetupUser();
            AuthenticateResult authenticateResult = await _sut.AuthenticateAsync();

            Assert.NotNull(authorizationInfo.User);
            Assert.True(authenticateResult.Principal?.HasClaim(ClaimTypes.Name, authorizationInfo.User.Username));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task HandleAuthenticateAsyncShouldAssignRoleClaim(bool isAdmin)
        {
            AuthorizationInfo authorizationInfo = SetupUser(isAdmin);
            AuthenticateResult authenticateResult = await _sut.AuthenticateAsync();

            Assert.NotNull(authorizationInfo.User);
            string expectedRole = authorizationInfo.User.HasPermission(PermissionKind.IsAdministrator) ? UserRoles.Administrator : UserRoles.User;
            Assert.True(authenticateResult.Principal?.HasClaim(ClaimTypes.Role, expectedRole));
        }

        [Fact]
        public async Task HandleAuthenticateAsyncShouldAssignTicketCorrectScheme()
        {
            SetupUser();
            AuthenticateResult authenticatedResult = await _sut.AuthenticateAsync();

            Assert.Equal(_scheme.Name, authenticatedResult.Ticket?.AuthenticationScheme);
        }

        private AuthorizationInfo SetupUser(bool isAdmin = false)
        {
            AuthorizationInfo authorizationInfo = _fixture.Create<AuthorizationInfo>();
            authorizationInfo.User = _fixture.Create<User>();
            authorizationInfo.User.AddDefaultPermissions();
            authorizationInfo.User.AddDefaultPreferences();
            authorizationInfo.User.SetPermission(PermissionKind.IsAdministrator, isAdmin);
            authorizationInfo.IsApiKey = false;
            authorizationInfo.Token = "fake-token";

            _jellyfinAuthServiceMock.Setup(
                    a => a.Authenticate(
                        It.IsAny<HttpRequest>()))
                .Returns(Task.FromResult(authorizationInfo));

            return authorizationInfo;
        }

        private void AllowFixtureCircularDependencies()
        {
            // A circular dependency exists in the User entity around parent folders,
            // this allows Autofixture to generate a User regardless, rather than throw
            // an error.
            _fixture.Behaviors.OfType<ThrowingRecursionBehavior>().ToList()
                .ForEach(b => _fixture.Behaviors.Remove(b));
            _fixture.Behaviors.Add(new OmitOnRecursionBehavior());
        }
    }
}
