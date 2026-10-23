using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Jellyfin.Api.Models.VisularrDtos;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Api.Helpers;

/// <summary>
/// The hub (linkarr) that manages visularr's users.
/// </summary>
public class ManagedHub
{
    private const string ConfigFileName = "visularr-managed.json";
    private const int MaxNameLength = 100;
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger<ManagedHub> _logger;
    private readonly string _configPath;
    private readonly Lock _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ManagedHub"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{ManagedHub}"/> interface.</param>
    public ManagedHub(IApplicationPaths applicationPaths, ILogger<ManagedHub> logger)
    {
        _logger = logger;
        _configPath = Path.Combine(applicationPaths.ConfigurationDirectoryPath, ConfigFileName);
    }

    /// <summary>
    /// Gets the managing hub.
    /// </summary>
    /// <returns>The <see cref="ManagedDto"/>, empty when the users are managed here.</returns>
    public ManagedDto Get()
    {
        lock (_lock)
        {
            if (!File.Exists(_configPath))
            {
                return new ManagedDto();
            }

            try
            {
                return JsonSerializer.Deserialize<ManagedDto>(File.ReadAllText(_configPath), _jsonOptions) ?? new ManagedDto();
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Ignoring the unreadable managed hub file {Path}", _configPath);
                return new ManagedDto();
            }
        }
    }

    /// <summary>
    /// Stores the managing hub.
    /// </summary>
    /// <param name="name">The hub's name.</param>
    /// <param name="url">The hub's url.</param>
    /// <returns>False when the name is blank or too long, or the url is not an absolute http(s) url.</returns>
    public bool TrySave(string? name, string? url)
    {
        string? normalized = NormalizeUrl(url);
        name = name?.Trim();
        if (normalized is null || string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            return false;
        }

        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
            // Write then rename, so a crash never leaves a half written file.
            string tempPath = _configPath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(new ManagedDto { Name = name, Url = normalized }, _jsonOptions));
            File.Move(tempPath, _configPath, true);
        }

        return true;
    }

    /// <summary>
    /// Normalizes a hub url: absolute http(s), no credentials, no query, no trailing slash.
    /// </summary>
    /// <param name="url">The url to check.</param>
    /// <returns>The normalized url, or null when it is not acceptable.</returns>
    public static string? NormalizeUrl(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    /// <summary>
    /// Forgets the managing hub: the users are managed here again.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            File.Delete(_configPath);
        }
    }
}
