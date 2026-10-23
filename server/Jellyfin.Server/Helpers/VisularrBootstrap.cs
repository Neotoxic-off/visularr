using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Entities.Security;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Helpers;

/// <summary>
/// Headless first-run setup for visularr when it is provisioned by linkarr (<c>VISULARR_API_KEY</c> is set).
/// </summary>
public sealed class VisularrBootstrap
{
    /// <summary>
    /// The name the auto-created Jellyfin admin is renamed to; linkarr deletes it after provisioning its own admin.
    /// </summary>
    public const string BootstrapUserName = "visularr-bootstrap";

    /// <summary>
    /// The app name of the API key linkarr uses.
    /// </summary>
    public const string ApiKeyAppName = "linkarr";

    private const string PasswordAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    private readonly IServiceProvider _services;
    private readonly ILogger _logger;
    private readonly string _apiKey;
    private readonly string _metadataLanguage;
    private readonly string _metadataCountry;
    private readonly string _mediaRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="VisularrBootstrap"/> class.
    /// </summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="apiKey">The validated API key token.</param>
    /// <param name="metadataLanguage">The preferred metadata language.</param>
    /// <param name="metadataCountry">The metadata country code.</param>
    /// <param name="mediaRoot">The folder holding the movies, series and anime folders.</param>
    public VisularrBootstrap(IServiceProvider services, ILogger logger, string apiKey, string metadataLanguage, string metadataCountry, string mediaRoot)
    {
        _services = services;
        _logger = logger;
        _apiKey = apiKey;
        _metadataLanguage = metadataLanguage;
        _metadataCountry = metadataCountry;
        _mediaRoot = mediaRoot;
    }

    /// <summary>
    /// Reads and validates <c>VISULARR_API_KEY</c>.
    /// </summary>
    /// <returns>The key, or <c>null</c> when unset (standalone mode).</returns>
    /// <exception cref="InvalidOperationException">The value is not at least 32 ASCII letters or digits.</exception>
    public static string? ReadApiKey()
    {
        string? key = Environment.GetEnvironmentVariable("VISULARR_API_KEY");
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (!IsValidApiKey(key))
        {
            // Never echo the value: it is a credential.
            throw new InvalidOperationException("VISULARR_API_KEY is invalid: it must be at least 32 characters of [A-Za-z0-9].");
        }

        return key;
    }

    /// <summary>
    /// Checks the API key format.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>Whether the key is at least 32 ASCII letters or digits.</returns>
    public static bool IsValidApiKey(string key) => key.Length >= 32 && key.All(char.IsAsciiLetterOrDigit);

    /// <summary>
    /// Creates a bootstrap from the environment.
    /// </summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="apiKey">The validated API key from <see cref="ReadApiKey"/>.</param>
    /// <returns>The bootstrap.</returns>
    public static VisularrBootstrap FromEnvironment(IServiceProvider services, ILogger logger, string apiKey)
    {
        string language = Environment.GetEnvironmentVariable("VISULARR_METADATA_LANGUAGE") is { Length: > 0 } l ? l : "en";
        string country = Environment.GetEnvironmentVariable("VISULARR_METADATA_COUNTRY") is { Length: > 0 } c ? c : "US";
        return new VisularrBootstrap(services, logger, apiKey, language, country, "/media");
    }

    /// <summary>
    /// Ensures the linkarr API key, locks down the bootstrap admin and completes the startup wizard. Idempotent.
    /// </summary>
    /// <returns><c>true</c> when the wizard was completed by this call and a library scan should be queued.</returns>
    public async Task<bool> RunAsync()
    {
        await EnsureApiKeyAsync().ConfigureAwait(false);

        IServerConfigurationManager config = _services.GetRequiredService<IServerConfigurationManager>();
        bool wizardCompleted = config.Configuration.IsStartupWizardCompleted;

        await SecureBootstrapUserAsync(wizardCompleted).ConfigureAwait(false);

        if (wizardCompleted)
        {
            return false;
        }

        config.Configuration.UICulture = "en-US";
        config.Configuration.PreferredMetadataLanguage = _metadataLanguage;
        config.Configuration.MetadataCountryCode = _metadataCountry;
        config.SaveConfiguration();

        await CreateDefaultLibrariesAsync().ConfigureAwait(false);

        config.Configuration.IsStartupWizardCompleted = true;
        config.SaveConfiguration();
        _logger.LogInformation("visularr: startup wizard completed headlessly");
        return true;
    }

    private async Task EnsureApiKeyAsync()
    {
        IDbContextFactory<JellyfinDbContext> dbProvider = _services.GetRequiredService<IDbContextFactory<JellyfinDbContext>>();
        JellyfinDbContext dbContext = await dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            if (await dbContext.ApiKeys.AnyAsync(k => k.AccessToken == _apiKey).ConfigureAwait(false))
            {
                return;
            }

            // Same entity AuthenticationManager.CreateApiKey adds, with the token fixed by the compose file.
            dbContext.ApiKeys.Add(new ApiKey(ApiKeyAppName) { AccessToken = _apiKey });
            await dbContext.SaveChangesAsync().ConfigureAwait(false);
            _logger.LogInformation("visularr: created the {AppName} API key", ApiKeyAppName);
        }
    }

    private async Task SecureBootstrapUserAsync(bool wizardCompleted)
    {
        IUserManager userManager = _services.GetRequiredService<IUserManager>();
        User? user = userManager.GetUserByName(BootstrapUserName);
        if (user is null)
        {
            // After the wizard, a missing bootstrap user means linkarr already removed it: never touch other users.
            if (wizardCompleted)
            {
                return;
            }

            await userManager.InitializeAsync().ConfigureAwait(false);
            User first = userManager.GetFirstUser() ?? throw new InvalidOperationException("No user exists after initialization.");
            await userManager.RenameUser(first.Id, first.Username, BootstrapUserName).ConfigureAwait(false);
            user = userManager.GetUserById(first.Id) ?? throw new InvalidOperationException("The bootstrap user vanished after renaming.");
        }

        user.SetPermission(PermissionKind.IsHidden, true);
        await userManager.UpdateUserAsync(user).ConfigureAwait(false);

        // Random, never logged or stored in clear: the account only exists until linkarr deletes it.
        await userManager.ChangePassword(user.Id, RandomNumberGenerator.GetString(PasswordAlphabet, 64)).ConfigureAwait(false);
    }

    private async Task CreateDefaultLibrariesAsync()
    {
        ILibraryManager libraryManager = _services.GetRequiredService<ILibraryManager>();
        HashSet<string> existing = libraryManager.GetVirtualFolders().Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        (string Name, CollectionTypeOptions Type, string Folder)[] libraries =
        [
            ("Movies", CollectionTypeOptions.movies, "movies"),
            ("Shows", CollectionTypeOptions.tvshows, "series"),
            ("Anime", CollectionTypeOptions.tvshows, "anime"),
        ];

        foreach ((string name, CollectionTypeOptions type, string folder) in libraries)
        {
            string path = Path.Combine(_mediaRoot, folder);
            if (existing.Contains(name) || !Directory.Exists(path))
            {
                continue;
            }

            LibraryOptions options = new LibraryOptions
            {
                PathInfos = [new MediaPathInfo(path)],
                ExtractTrickplayImagesDuringLibraryScan = false,
                ExtractChapterImagesDuringLibraryScan = false,
                EnableRealtimeMonitor = true,
            };
            await libraryManager.AddVirtualFolder(name, type, options, false).ConfigureAwait(false);
            _logger.LogInformation("visularr: created library {Name} at {Path}", name, path);
        }
    }
}
