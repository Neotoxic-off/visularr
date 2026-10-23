using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Jellyfin.Extensions;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;
using Nikse.SubtitleEdit.Core.Common;
using SubtitleFormat = Nikse.SubtitleEdit.Core.SubtitleFormats.SubtitleFormat;

namespace MediaBrowser.MediaEncoding.Subtitles
{
    /// <summary>
    /// SubStation Alpha subtitle parser.
    /// </summary>
    public class SubtitleEditParser : ISubtitleParser
    {
        private readonly ILogger<SubtitleEditParser> _logger;
        private readonly Dictionary<string, List<Type>> _subtitleFormatTypes;

        /// <summary>
        /// Initializes a new instance of the <see cref="SubtitleEditParser"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        public SubtitleEditParser(ILogger<SubtitleEditParser> logger)
        {
            _logger = logger;
            _subtitleFormatTypes = GetSubtitleFormatTypes();
        }

        /// <inheritdoc />
        public SubtitleTrackInfo Parse(Stream stream, string fileExtension)
        {
            Subtitle subtitle = new Subtitle();
            List<string> lines = stream.ReadAllLines().ToList();

            if (!_subtitleFormatTypes.TryGetValue(fileExtension, out List<Type>? subtitleFormatTypesForExtension))
            {
                throw new ArgumentException($"Unsupported file extension: {fileExtension}", nameof(fileExtension));
            }

            foreach (Type subtitleFormatType in subtitleFormatTypesForExtension)
            {
                SubtitleFormat subtitleFormat = (SubtitleFormat)Activator.CreateInstance(subtitleFormatType, true)!;
                _logger.LogDebug(
                    "Trying to parse '{FileExtension}' subtitle using the {SubtitleFormatParser} format parser",
                    fileExtension,
                    subtitleFormat.Name);
                subtitleFormat.LoadSubtitle(subtitle, lines, fileExtension);
                if (subtitleFormat.ErrorCount == 0)
                {
                    break;
                }
                else if (subtitleFormat.TryGetErrors(out string? errors))
                {
                    _logger.LogError(
                        "{ErrorCount} errors encountered while parsing '{FileExtension}' subtitle using the {SubtitleFormatParser} format parser, errors: {Errors}",
                        subtitleFormat.ErrorCount,
                        fileExtension,
                        subtitleFormat.Name,
                        errors);
                }
                else
                {
                    _logger.LogError(
                        "{ErrorCount} errors encountered while parsing '{FileExtension}' subtitle using the {SubtitleFormatParser} format parser",
                        subtitleFormat.ErrorCount,
                        fileExtension,
                        subtitleFormat.Name);
                }
            }

            if (subtitle.Paragraphs.Count == 0)
            {
                throw new ArgumentException("Unsupported format: " + fileExtension);
            }

            SubtitleTrackInfo trackInfo = new SubtitleTrackInfo();
            int len = subtitle.Paragraphs.Count;
            SubtitleTrackEvent[] trackEvents = new SubtitleTrackEvent[len];
            for (int i = 0; i < len; i++)
            {
                Paragraph p = subtitle.Paragraphs[i];
                trackEvents[i] = new SubtitleTrackEvent(p.Number.ToString(CultureInfo.InvariantCulture), p.Text)
                {
                    StartPositionTicks = p.StartTime.TimeSpan.Ticks,
                    EndPositionTicks = p.EndTime.TimeSpan.Ticks
                };
            }

            trackInfo.TrackEvents = trackEvents;
            return trackInfo;
        }

        /// <inheritdoc />
        public bool SupportsFileExtension(string fileExtension)
            => _subtitleFormatTypes.ContainsKey(fileExtension);

        private Dictionary<string, List<Type>> GetSubtitleFormatTypes()
        {
            Dictionary<string, List<Type>> subtitleFormatTypes = new Dictionary<string, List<Type>>(StringComparer.OrdinalIgnoreCase);
            Assembly assembly = typeof(SubtitleFormat).Assembly;

            foreach (Type type in assembly.GetTypes())
            {
                if (!type.IsSubclassOf(typeof(SubtitleFormat)) || type.IsAbstract)
                {
                    continue;
                }

                try
                {
                    SubtitleFormat tempInstance = (SubtitleFormat)Activator.CreateInstance(type, true)!;
                    string extension = tempInstance.Extension.TrimStart('.');
                    if (!string.IsNullOrEmpty(extension))
                    {
                        // Store only the type, we will instantiate from it later
                        if (!subtitleFormatTypes.TryGetValue(extension, out List<Type>? subtitleFormatTypesForExtension))
                        {
                            subtitleFormatTypes[extension] = [type];
                        }
                        else
                        {
                            subtitleFormatTypesForExtension.Add(type);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create instance of the subtitle format {SubtitleFormatType}", type.Name);
                }
            }

            return subtitleFormatTypes;
        }
    }
}
