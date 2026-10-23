#pragma warning disable CS1591

using System;
using System.Globalization;
using System.IO;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Configuration;

namespace MediaBrowser.MediaEncoding.Configuration
{
    public class EncodingConfigurationStore : ConfigurationStore, IValidatingConfiguration
    {
        public EncodingConfigurationStore()
        {
            ConfigurationType = typeof(EncodingOptions);
            Key = "encoding";
        }

        public void Validate(object oldConfig, object newConfig)
        {
            EncodingOptions oldEncodingOptions = (EncodingOptions)oldConfig;
            EncodingOptions newEncodingOptions = (EncodingOptions)newConfig;

            ArgumentNullException.ThrowIfNull(oldEncodingOptions, nameof(oldConfig));
            ArgumentNullException.ThrowIfNull(newEncodingOptions, nameof(newConfig));

            string newPath = newEncodingOptions.TranscodingTempPath;

            if (!string.IsNullOrWhiteSpace(newPath)
                && !string.Equals(oldEncodingOptions.TranscodingTempPath, newPath, StringComparison.Ordinal))
            {
                // Validate
                if (!Directory.Exists(newPath))
                {
                    throw new DirectoryNotFoundException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "{0} does not exist.",
                            newPath));
                }
            }

            if (!string.IsNullOrWhiteSpace(newEncodingOptions.EncoderAppPath)
                && !string.Equals(oldEncodingOptions.EncoderAppPath, newEncodingOptions.EncoderAppPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unable to update encoder app path.");
            }
        }
    }
}
