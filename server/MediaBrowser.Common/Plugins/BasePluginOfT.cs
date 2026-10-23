#nullable disable
#pragma warning disable SA1649 // File name should match first type name

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Common.Plugins
{
    /// <summary>
    /// Provides a common base class for all plugins.
    /// </summary>
    /// <typeparam name="TConfigurationType">The type of the T configuration type.</typeparam>
    public abstract class BasePlugin<TConfigurationType> : BasePlugin, IHasPluginConfiguration
        where TConfigurationType : BasePluginConfiguration
    {
        /// <summary>
        /// The configuration sync lock.
        /// </summary>
        private readonly Lock _configurationSyncLock = new();

        /// <summary>
        /// The configuration save lock.
        /// </summary>
        private readonly Lock _configurationSaveLock = new();

        /// <summary>
        /// The configuration.
        /// </summary>
        private TConfigurationType _configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="BasePlugin{TConfigurationType}" /> class.
        /// </summary>
        /// <param name="applicationPaths">The application paths.</param>
        /// <param name="xmlSerializer">The XML serializer.</param>
        protected BasePlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        {
            ApplicationPaths = applicationPaths;
            XmlSerializer = xmlSerializer;

            Assembly assembly = GetType().Assembly;
            AssemblyName assemblyName = assembly.GetName();
            string assemblyFilePath = assembly.Location;

            string dataFolderPath = Path.Combine(ApplicationPaths.PluginsPath, Path.GetFileNameWithoutExtension(assemblyFilePath));
            if (Version is not null && !Directory.Exists(dataFolderPath))
            {
                // Try again with the version number appended to the folder name.
                dataFolderPath += "_" + Version;
            }

            SetAttributes(assemblyFilePath, dataFolderPath, assemblyName.Version);

            object[] idAttributes = assembly.GetCustomAttributes(typeof(GuidAttribute), true);
            if (idAttributes.Length > 0)
            {
                GuidAttribute attribute = (GuidAttribute)idAttributes[0];
                Guid assemblyId = new Guid(attribute.Value);

                SetId(assemblyId);
            }
        }

        /// <summary>
        /// Gets the application paths.
        /// </summary>
        /// <value>The application paths.</value>
        protected IApplicationPaths ApplicationPaths { get; private set; }

        /// <summary>
        /// Gets the XML serializer.
        /// </summary>
        /// <value>The XML serializer.</value>
        protected IXmlSerializer XmlSerializer { get; private set; }

        /// <summary>
        /// Gets the type of configuration this plugin uses.
        /// </summary>
        /// <value>The type of the configuration.</value>
        public Type ConfigurationType => typeof(TConfigurationType);

        /// <summary>
        /// Gets or sets the event handler that is triggered when this configuration changes.
        /// </summary>
        public EventHandler<BasePluginConfiguration> ConfigurationChanged { get; set; }

        /// <summary>
        /// Gets the name the assembly file.
        /// </summary>
        /// <value>The name of the assembly file.</value>
        protected string AssemblyFileName => Path.GetFileName(AssemblyFilePath);

        /// <summary>
        /// Gets or sets the plugin configuration.
        /// </summary>
        /// <value>The configuration.</value>
        public TConfigurationType Configuration
        {
            get
            {
                // Lazy load
                if (_configuration is null)
                {
                    lock (_configurationSyncLock)
                    {
                        _configuration ??= LoadConfiguration();
                    }
                }

                return _configuration;
            }

            protected set => _configuration = value;
        }

        /// <summary>
        /// Gets the name of the configuration file. Subclasses should override.
        /// </summary>
        /// <value>The name of the configuration file.</value>
        public virtual string ConfigurationFileName => Path.ChangeExtension(AssemblyFileName, ".xml");

        /// <summary>
        /// Gets the full path to the configuration file.
        /// </summary>
        /// <value>The configuration file path.</value>
        public string ConfigurationFilePath => Path.Combine(ApplicationPaths.PluginConfigurationsPath, ConfigurationFileName);

        /// <summary>
        /// Gets the plugin configuration.
        /// </summary>
        /// <value>The configuration.</value>
        BasePluginConfiguration IHasPluginConfiguration.Configuration => Configuration;

        /// <summary>
        /// Saves the current configuration to the file system.
        /// </summary>
        /// <param name="config">Configuration to save.</param>
        public virtual void SaveConfiguration(TConfigurationType config)
        {
            lock (_configurationSaveLock)
            {
                string folder = Path.GetDirectoryName(ConfigurationFilePath);
                Directory.CreateDirectory(folder);

                XmlSerializer.SerializeToFile(config, ConfigurationFilePath);
            }
        }

        /// <summary>
        /// Saves the current configuration to the file system.
        /// </summary>
        public virtual void SaveConfiguration()
        {
            SaveConfiguration(Configuration);
        }

        /// <inheritdoc />
        public virtual void UpdateConfiguration(BasePluginConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            Configuration = (TConfigurationType)configuration;

            SaveConfiguration(Configuration);

            ConfigurationChanged?.Invoke(this, configuration);
        }

        /// <inheritdoc />
        public override PluginInfo GetPluginInfo()
        {
            PluginInfo info = base.GetPluginInfo();

            info.ConfigurationFileName = ConfigurationFileName;

            return info;
        }

        private TConfigurationType LoadConfiguration()
        {
            string path = ConfigurationFilePath;

            try
            {
                return (TConfigurationType)XmlSerializer.DeserializeFromFile(typeof(TConfigurationType), path);
            }
            catch
            {
                TConfigurationType config = Activator.CreateInstance<TConfigurationType>();
                SaveConfiguration(config);
                return config;
            }
        }
    }
}
