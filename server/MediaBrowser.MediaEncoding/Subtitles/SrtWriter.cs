using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using MediaBrowser.Model.MediaInfo;

namespace MediaBrowser.MediaEncoding.Subtitles
{
    /// <summary>
    /// SRT subtitle writer.
    /// </summary>
    public partial class SrtWriter : ISubtitleWriter
    {
        [GeneratedRegex(@"\\n", RegexOptions.IgnoreCase)]
        private static partial Regex NewLineEscapedRegex();

        /// <inheritdoc />
        public void Write(SubtitleTrackInfo info, Stream stream, CancellationToken cancellationToken)
        {
            using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8, 1024, true))
            {
                IReadOnlyList<SubtitleTrackEvent> trackEvents = info.TrackEvents;

                for (int i = 0; i < trackEvents.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    SubtitleTrackEvent trackEvent = trackEvents[i];

                    writer.WriteLine((i + 1).ToString(CultureInfo.InvariantCulture));
                    writer.WriteLine(
                        @"{0:hh\:mm\:ss\,fff} --> {1:hh\:mm\:ss\,fff}",
                        TimeSpan.FromTicks(trackEvent.StartPositionTicks),
                        TimeSpan.FromTicks(trackEvent.EndPositionTicks));

                    string text = trackEvent.Text;

                    // TODO: Not sure how to handle these
                    text = NewLineEscapedRegex().Replace(text, " ");

                    writer.WriteLine(text);
                    writer.WriteLine();
                }
            }
        }
    }
}
