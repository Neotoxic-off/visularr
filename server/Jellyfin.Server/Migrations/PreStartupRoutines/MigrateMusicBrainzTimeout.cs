using System;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using Emby.Server.Implementations;
using MediaBrowser.Providers.Plugins.MusicBrainz.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Migrations.PreStartupRoutines;

/// <inheritdoc />
#pragma warning disable CS0618 // Type or member is obsolete
[JellyfinMigration("2025-04-20T02:00:00", nameof(MigrateMusicBrainzTimeout), "A6DCACF4-C057-4Ef9-80D3-61CEF9DDB4F0", Stage = Stages.JellyfinMigrationStageTypes.PreInitialisation)]
public class MigrateMusicBrainzTimeout : IMigrationRoutine
#pragma warning restore CS0618 // Type or member is obsolete
{
    private readonly ServerApplicationPaths _applicationPaths;
    private readonly ILogger<MigrateMusicBrainzTimeout> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MigrateMusicBrainzTimeout"/> class.
    /// </summary>
    /// <param name="applicationPaths">An instance of <see cref="ServerApplicationPaths"/>.</param>
    /// <param name="loggerFactory">An instance of the <see cref="ILoggerFactory"/> interface.</param>
    public MigrateMusicBrainzTimeout(ServerApplicationPaths applicationPaths, ILoggerFactory loggerFactory)
    {
        _applicationPaths = applicationPaths;
        _logger = loggerFactory.CreateLogger<MigrateMusicBrainzTimeout>();
    }

    /// <inheritdoc />
    public void Perform()
    {
        string path = Path.Combine(_applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.MusicBrainz.xml");
        if (!File.Exists(path))
        {
            _logger.LogDebug("No MusicBrainz plugin configuration file found, skipping");
            return;
        }

        OldMusicBrainzConfiguration? oldPluginConfiguration = ReadOld(path);

        if (oldPluginConfiguration is not null)
        {
            PluginConfiguration newPluginConfiguration = new PluginConfiguration
            {
                Server = oldPluginConfiguration.Server,
                ReplaceArtistName = oldPluginConfiguration.ReplaceArtistName
            };
            double newRateLimit = oldPluginConfiguration.RateLimit / 1000.0;
            newPluginConfiguration.RateLimit = newRateLimit < 1.0 ? 1.0 : newRateLimit;
            WriteNew(path, newPluginConfiguration);
        }
    }

    private OldMusicBrainzConfiguration? ReadOld(string path)
    {
        using XmlReader xmlReader = XmlReader.Create(path);
        XmlSerializer serverConfigSerializer = new XmlSerializer(typeof(OldMusicBrainzConfiguration), new XmlRootAttribute("PluginConfiguration"));
        return serverConfigSerializer.Deserialize(xmlReader) as OldMusicBrainzConfiguration;
    }

    private void WriteNew(string path, PluginConfiguration newPluginConfiguration)
    {
        XmlSerializer pluginConfigurationSerializer = new XmlSerializer(typeof(PluginConfiguration), new XmlRootAttribute("PluginConfiguration"));
        XmlWriterSettings xmlWriterSettings = new XmlWriterSettings { Indent = true };
        using XmlWriter xmlWriter = XmlWriter.Create(path, xmlWriterSettings);
        pluginConfigurationSerializer.Serialize(xmlWriter, newPluginConfiguration);
    }

#pragma warning disable
    public sealed class OldMusicBrainzConfiguration
    {
        private string _server = string.Empty;

        private long _rateLimit = 0L;

        public string Server
        {
            get => _server;
            set => _server = value.TrimEnd('/');
        }

        public long RateLimit
        {
            get => _rateLimit;
            set => _rateLimit = value;
        }

        public bool ReplaceArtistName { get; set; }
    }
}
