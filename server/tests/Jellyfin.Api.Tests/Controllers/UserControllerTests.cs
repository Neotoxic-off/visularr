using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoFixture.Xunit2;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Controllers;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Server.Implementations.Users;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.QuickConnect;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Nikse.SubtitleEdit.Core.Common;
using Xunit;

namespace Jellyfin.Api.Tests.Controllers;

public class UserControllerTests
{
    private readonly UserController _subject;
    private readonly Mock<IUserManager> _mockUserManager;
    private readonly Mock<ISessionManager> _mockSessionManager;
    private readonly Mock<INetworkManager> _mockNetworkManager;
    private readonly Mock<IDeviceManager> _mockDeviceManager;
    private readonly Mock<IAuthorizationContext> _mockAuthorizationContext;
    private readonly Mock<IServerConfigurationManager> _mockServerConfigurationManager;
    private readonly Mock<ILogger<UserController>> _mockLogger;
    private readonly Mock<IQuickConnect> _mockQuickConnect;
    private readonly Mock<IPlaylistManager> _mockPlaylistManager;

    public UserControllerTests()
    {
        _mockUserManager = new Mock<IUserManager>();
        _mockSessionManager = new Mock<ISessionManager>();
        _mockNetworkManager = new Mock<INetworkManager>();
        _mockDeviceManager = new Mock<IDeviceManager>();
        _mockAuthorizationContext = new Mock<IAuthorizationContext>();
        _mockServerConfigurationManager = new Mock<IServerConfigurationManager>();
        _mockLogger = new Mock<ILogger<UserController>>();
        _mockQuickConnect = new Mock<IQuickConnect>();
        _mockPlaylistManager = new Mock<IPlaylistManager>();

        _subject = new UserController(
            _mockUserManager.Object,
            _mockSessionManager.Object,
            _mockNetworkManager.Object,
            _mockDeviceManager.Object,
            _mockAuthorizationContext.Object,
            _mockServerConfigurationManager.Object,
            _mockLogger.Object,
            _mockQuickConnect.Object,
            _mockPlaylistManager.Object);
    }

    [Theory]
    [AutoData]
    public async Task UpdateUserPolicy_WhenUserNotFound_ReturnsNotFound(Guid userId, UserPolicy userPolicy)
    {
        User? nullUser = null;
        _mockUserManager.
            Setup(m => m.GetUserById(userId))
            .Returns(nullUser);

        Assert.IsType<NotFoundResult>(await _subject.UpdateUserPolicy(userId, userPolicy));
    }

    [Theory]
    [InlineAutoData(null)]
    [InlineAutoData("")]
    [InlineAutoData("   ")]
    public void UpdateUserPolicy_WhenPasswordResetProviderIdNotSupplied_ReturnsBadRequest(string? passwordResetProviderId)
    {
        UserPolicy userPolicy = new UserPolicy
        {
            PasswordResetProviderId = passwordResetProviderId,
            AuthenticationProviderId = "AuthenticationProviderId"
        };

        Assert.Contains(
            Validate(userPolicy), v =>
                v.MemberNames.Contains("PasswordResetProviderId") &&
                v.ErrorMessage is not null &&
                v.ErrorMessage.Contains("required", StringComparison.CurrentCultureIgnoreCase));
    }

    [Theory]
    [InlineAutoData(null)]
    [InlineAutoData("")]
    [InlineAutoData("   ")]
    public void UpdateUserPolicy_WhenAuthenticationProviderIdNotSupplied_ReturnsBadRequest(string? authenticationProviderId)
    {
        UserPolicy userPolicy = new UserPolicy
        {
            AuthenticationProviderId = authenticationProviderId,
            PasswordResetProviderId = "PasswordResetProviderId"
        };

        Assert.Contains(Validate(userPolicy), v =>
            v.MemberNames.Contains("AuthenticationProviderId") &&
            v.ErrorMessage is not null &&
            v.ErrorMessage.Contains("required", StringComparison.CurrentCultureIgnoreCase));
    }

    // visularr: a user keeps self-service preferences but cannot rename itself (linkarr links accounts by name).
    [Theory]
    [InlineData(UserRoles.User, "bob", false)]
    [InlineData(UserRoles.User, "alice", true)]
    [InlineData(UserRoles.Administrator, "bob", true)]
    public async Task UpdateUser_OnlyAnAdministratorRenames(string role, string newName, bool allowed)
    {
        User user = new User("alice", typeof(DefaultAuthenticationProvider).FullName!, typeof(DefaultPasswordResetProvider).FullName!);
        user.AddDefaultPermissions();
        user.AddDefaultPreferences();
        _mockUserManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        Claim[] claims = new[]
        {
            new Claim(ClaimTypes.Role, role),
            new Claim(InternalClaimTypes.UserId, user.Id.ToString("N", CultureInfo.InvariantCulture)),
        };
        _subject.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) } };

        ActionResult result = await _subject.UpdateUser(null, new UserDto { Name = newName, Configuration = new UserConfiguration() });

        Assert.Equal(allowed, result is NoContentResult);
        _mockUserManager.Verify(m => m.RenameUser(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()), newName != "alice" && allowed ? Times.Once() : Times.Never());
    }

    private List<ValidationResult> Validate(object model)
    {
        List<ValidationResult> result = new List<ValidationResult>();
        ValidationContext context = new ValidationContext(model, null, null);
        Validator.TryValidateObject(model, context, result, true);

        return result;
    }
}
