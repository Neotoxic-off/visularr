using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ATL;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Lyrics;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;
using static Jellyfin.Extensions.StringExtensions;

namespace MediaBrowser.Providers.MediaInfo
{
    /// <summary>
    /// Probes audio files for metadata.
    /// </summary>
    public class AudioFileProber
    {
        private const char InternalValueSeparator = '\u001F';

        private readonly IMediaEncoder _mediaEncoder;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<AudioFileProber> _logger;
        private readonly IMediaSourceManager _mediaSourceManager;
        private readonly LyricResolver _lyricResolver;
        private readonly ILyricManager _lyricManager;
        private readonly IMediaStreamRepository _mediaStreamRepository;

        /// <summary>
        /// Initializes a new instance of the <see cref="AudioFileProber"/> class.
        /// </summary>
        /// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
        /// <param name="mediaSourceManager">Instance of the <see cref="IMediaSourceManager"/> interface.</param>
        /// <param name="mediaEncoder">Instance of the <see cref="IMediaEncoder"/> interface.</param>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
        /// <param name="lyricResolver">Instance of the <see cref="LyricResolver"/> interface.</param>
        /// <param name="lyricManager">Instance of the <see cref="ILyricManager"/> interface.</param>
        /// <param name="mediaStreamRepository">Instance of the <see cref="IMediaStreamRepository"/>.</param>
        public AudioFileProber(
            ILogger<AudioFileProber> logger,
            IMediaSourceManager mediaSourceManager,
            IMediaEncoder mediaEncoder,
            ILibraryManager libraryManager,
            LyricResolver lyricResolver,
            ILyricManager lyricManager,
            IMediaStreamRepository mediaStreamRepository)
        {
            _mediaEncoder = mediaEncoder;
            _libraryManager = libraryManager;
            _logger = logger;
            _mediaSourceManager = mediaSourceManager;
            _lyricResolver = lyricResolver;
            _lyricManager = lyricManager;
            _mediaStreamRepository = mediaStreamRepository;
            ATL.Settings.DisplayValueSeparator = InternalValueSeparator;
            ATL.Settings.UseFileNameWhenNoTitle = false;
            ATL.Settings.ID3v2_separatev2v3Values = false;
        }

        /// <summary>
        /// Probes the specified item for metadata.
        /// </summary>
        /// <param name="item">The item to probe.</param>
        /// <param name="options">The <see cref="MetadataRefreshOptions"/>.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/>.</param>
        /// <typeparam name="T">The type of item to resolve.</typeparam>
        /// <returns>A <see cref="Task"/> probing the item for metadata.</returns>
        public async Task<ItemUpdateType> Probe<T>(
            T item,
            MetadataRefreshOptions options,
            CancellationToken cancellationToken)
            where T : Audio
        {
            string path = item.Path;
            MediaProtocol protocol = item.PathProtocol ?? MediaProtocol.File;

            if (!item.IsShortcut || options.EnableRemoteContentProbe)
            {
                if (item.IsShortcut)
                {
                    path = item.ShortcutPath;
                    protocol = _mediaSourceManager.GetPathProtocol(path);
                }

                Model.MediaInfo.MediaInfo result = await _mediaEncoder.GetMediaInfo(
                    new MediaInfoRequest
                    {
                        MediaType = DlnaProfileType.Audio,
                        MediaSource = new MediaSourceInfo
                        {
                            Path = path,
                            Protocol = protocol
                        }
                    },
                    cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                await FetchAsync(item, result, options, cancellationToken).ConfigureAwait(false);
            }

            return ItemUpdateType.MetadataImport;
        }

        /// <summary>
        /// Fetches the specified audio.
        /// </summary>
        /// <param name="audio">The <see cref="Audio"/>.</param>
        /// <param name="mediaInfo">The <see cref="Model.MediaInfo.MediaInfo"/>.</param>
        /// <param name="options">The <see cref="MetadataRefreshOptions"/>.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/>.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        private async Task FetchAsync(
            Audio audio,
            Model.MediaInfo.MediaInfo mediaInfo,
            MetadataRefreshOptions options,
            CancellationToken cancellationToken)
        {
            audio.Container = mediaInfo.Container;
            audio.TotalBitrate = mediaInfo.Bitrate;

            audio.RunTimeTicks = mediaInfo.RunTimeTicks;

            // Add external lyrics first to prevent the lrc file get overwritten on first scan
            List<MediaStream> mediaStreams = new List<MediaStream>(mediaInfo.MediaStreams);
            AddExternalLyrics(audio, mediaStreams, options);
            bool tryExtractEmbeddedLyrics = mediaStreams.All(s => s.Type != MediaStreamType.Lyric);

            if (!audio.IsLocked)
            {
                await FetchDataFromTags(audio, mediaInfo, options, tryExtractEmbeddedLyrics).ConfigureAwait(false);
                if (tryExtractEmbeddedLyrics)
                {
                    AddExternalLyrics(audio, mediaStreams, options);
                }
            }

            audio.HasLyrics = mediaStreams.Any(s => s.Type == MediaStreamType.Lyric);

            _mediaStreamRepository.SaveMediaStreams(audio.Id, mediaStreams, cancellationToken);
        }

        /// <summary>
        /// Fetches data from the tags.
        /// </summary>
        /// <param name="audio">The <see cref="Audio"/>.</param>
        /// <param name="mediaInfo">The <see cref="Model.MediaInfo.MediaInfo"/>.</param>
        /// <param name="options">The <see cref="MetadataRefreshOptions"/>.</param>
        /// <param name="tryExtractEmbeddedLyrics">Whether to extract embedded lyrics to lrc file. </param>
        private async Task FetchDataFromTags(Audio audio, Model.MediaInfo.MediaInfo mediaInfo, MetadataRefreshOptions options, bool tryExtractEmbeddedLyrics)
        {
            LibraryOptions libraryOptions = _libraryManager.GetLibraryOptions(audio);
            Track track = new Track(audio.Path);

            if (track.MetadataFormats
                .All(mf => string.Equals(mf.ShortName, "ID3v1", StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning("File {File} only has ID3v1 tags, some fields may be truncated", audio.Path);
            }

            // We should never use the property setter of the ATL.Track class.
            // That setter is meant for its own tag parser and external editor usage and will have unwanted side effects
            // For example, setting the Year property will also set the Date property, which is not what we want here.
            // To properly handle fallback values, we make a clone of those fields when valid.
            string? trackTitle = (string.IsNullOrEmpty(track.Title) ? mediaInfo.Name : track.Title)?.Trim();
            string? trackAlbum = (string.IsNullOrEmpty(track.Album) ? mediaInfo.Album : track.Album)?.Trim();
            int? trackYear = track.Year is null or 0 ? mediaInfo.ProductionYear : track.Year;
            int? trackTrackNumber = track.TrackNumber is null or 0 ? mediaInfo.IndexNumber : track.TrackNumber;
            int? trackDiscNumber = track.DiscNumber is null or 0 ? mediaInfo.ParentIndexNumber : track.DiscNumber;

            // Some users may use a misbehaved tag editor that writes a null character in the tag when not allowed by the standard.
            trackTitle = GetSanitizedStringTag(trackTitle, audio.Path);
            trackAlbum = GetSanitizedStringTag(trackAlbum, audio.Path);
            string? trackAlbumArtist = GetSanitizedStringTag(track.AlbumArtist, audio.Path);
            string? trackArist = GetSanitizedStringTag(track.Artist, audio.Path);
            string? trackComposer = GetSanitizedStringTag(track.Composer, audio.Path);
            string? trackGenre = GetSanitizedStringTag(track.Genre, audio.Path);

            if (audio.SupportsPeople && !audio.LockedFields.Contains(MetadataField.Cast))
            {
                List<PersonInfo> people = new List<PersonInfo>();
                string[]? albumArtists = null;
                if (libraryOptions.PreferNonstandardArtistsTag)
                {
                    TryGetSanitizedAdditionalFields(track, "ALBUMARTISTS", out string? albumArtistsTagString);
                    if (albumArtistsTagString is not null)
                    {
                        albumArtists = albumArtistsTagString.Split(InternalValueSeparator);
                    }
                }

                if (albumArtists is null || albumArtists.Length == 0)
                {
                    albumArtists = string.IsNullOrEmpty(trackAlbumArtist) ? [] : trackAlbumArtist.Split(InternalValueSeparator);
                }

                if (libraryOptions.UseCustomTagDelimiters)
                {
                    albumArtists = albumArtists.SelectMany(a => SplitWithCustomDelimiter(a, libraryOptions.GetCustomTagDelimiters(), libraryOptions.DelimiterWhitelist)).ToArray();
                }

                foreach (string albumArtist in albumArtists)
                {
                    if (!string.IsNullOrWhiteSpace(albumArtist))
                    {
                        PeopleHelper.AddPerson(people, new PersonInfo
                        {
                            Name = albumArtist,
                            Type = PersonKind.AlbumArtist
                        });
                    }
                }

                string[]? performers = null;
                if (libraryOptions.PreferNonstandardArtistsTag)
                {
                    TryGetSanitizedAdditionalFields(track, "ARTISTS", out string? artistsTagString);
                    if (artistsTagString is not null)
                    {
                        performers = artistsTagString.Split(InternalValueSeparator);
                    }
                }

                if (performers is null || performers.Length == 0)
                {
                    performers = string.IsNullOrEmpty(trackArist) ? [] : trackArist.Split(InternalValueSeparator);
                }

                if (libraryOptions.UseCustomTagDelimiters)
                {
                    performers = performers.SelectMany(p => SplitWithCustomDelimiter(p, libraryOptions.GetCustomTagDelimiters(), libraryOptions.DelimiterWhitelist)).ToArray();
                }

                foreach (string performer in performers)
                {
                    if (!string.IsNullOrWhiteSpace(performer))
                    {
                        PeopleHelper.AddPerson(people, new PersonInfo
                        {
                            Name = performer,
                            Type = PersonKind.Artist
                        });
                    }
                }

                if (!string.IsNullOrWhiteSpace(trackComposer))
                {
                    foreach (string composer in trackComposer.Split(InternalValueSeparator))
                    {
                        if (!string.IsNullOrWhiteSpace(composer))
                        {
                            PeopleHelper.AddPerson(people, new PersonInfo
                            {
                                Name = composer,
                                Type = PersonKind.Composer
                            });
                        }
                    }
                }

                _libraryManager.UpdatePeople(audio, people);

                if (options.ReplaceAllMetadata && performers.Length != 0)
                {
                    audio.Artists = performers;
                }
                else if (!options.ReplaceAllMetadata
                         && (audio.Artists is null || audio.Artists.Count == 0))
                {
                    audio.Artists = performers;
                }

                if (albumArtists.Length == 0)
                {
                    // Album artists not provided, fall back to performers (artists).
                    albumArtists = performers;
                }

                if (options.ReplaceAllMetadata && albumArtists.Length != 0)
                {
                    audio.AlbumArtists = albumArtists;
                }
                else if (!options.ReplaceAllMetadata
                         && (audio.AlbumArtists is null || audio.AlbumArtists.Count == 0))
                {
                    audio.AlbumArtists = albumArtists;
                }
            }

            if (!audio.LockedFields.Contains(MetadataField.Name) && !string.IsNullOrEmpty(trackTitle))
            {
                audio.Name = trackTitle;
            }

            if (options.ReplaceAllMetadata)
            {
                audio.Album = trackAlbum;
                audio.IndexNumber = trackTrackNumber;
                audio.ParentIndexNumber = trackDiscNumber;
            }
            else
            {
                audio.Album ??= trackAlbum;
                audio.IndexNumber ??= trackTrackNumber;
                audio.ParentIndexNumber ??= trackDiscNumber;
            }

            if (track.Date.HasValue)
            {
                audio.PremiereDate = track.Date;
            }

            if (trackYear.HasValue)
            {
                int year = trackYear.Value;
                audio.ProductionYear = year;

                // ATL library handles such fallback this with its own internal logic, but we also need to handle it here for the ffprobe fallbacks.
                if (!audio.PremiereDate.HasValue)
                {
                    try
                    {
                        audio.PremiereDate = new DateTime(year, 01, 01);
                    }
                    catch (ArgumentOutOfRangeException ex)
                    {
                        _logger.LogError(ex, "Error parsing YEAR tag in {File}. '{TagValue}' is an invalid year", audio.Path, trackYear);
                    }
                }
            }

            if (!audio.LockedFields.Contains(MetadataField.Genres))
            {
                string[] genres = string.IsNullOrEmpty(trackGenre) ? [] : trackGenre.Split(InternalValueSeparator).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

                if (libraryOptions.UseCustomTagDelimiters)
                {
                    genres = genres.SelectMany(g => SplitWithCustomDelimiter(g, libraryOptions.GetCustomTagDelimiters(), libraryOptions.DelimiterWhitelist)).ToArray();
                }

                genres = genres.Trimmed().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

                if (options.ReplaceAllMetadata || audio.Genres is null || audio.Genres.Length == 0 || audio.Genres.All(string.IsNullOrWhiteSpace))
                {
                    audio.Genres = genres;
                }
            }

            TryGetSanitizedAdditionalFields(track, "REPLAYGAIN_TRACK_GAIN", out string? trackGainTag);

            if (trackGainTag is not null)
            {
                if (trackGainTag.EndsWith("db", StringComparison.OrdinalIgnoreCase))
                {
                    trackGainTag = trackGainTag[..^2].Trim();
                }

                if (float.TryParse(trackGainTag, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value))
                {
                    audio.NormalizationGain = value;
                }
            }

            if (options.ReplaceAllMetadata || !audio.TryGetProviderId(MetadataProvider.MusicBrainzArtist, out _))
            {
                if ((TryGetSanitizedAdditionalFields(track, "MUSICBRAINZ_ARTISTID", out string? musicBrainzArtistTag)
                     || TryGetSanitizedAdditionalFields(track, "MusicBrainz Artist Id", out musicBrainzArtistTag))
                    && !string.IsNullOrEmpty(musicBrainzArtistTag))
                {
                    string? id = GetFirstMusicBrainzId(musicBrainzArtistTag, libraryOptions.UseCustomTagDelimiters, libraryOptions.GetCustomTagDelimiters(), libraryOptions.DelimiterWhitelist);
                    audio.TrySetProviderId(MetadataProvider.MusicBrainzArtist, id);
                }
            }

            if (options.ReplaceAllMetadata || !audio.TryGetProviderId(MetadataProvider.MusicBrainzAlbumArtist, out _))
            {
                if ((TryGetSanitizedAdditionalFields(track, "MUSICBRAINZ_ALBUMARTISTID", out string? musicBrainzReleaseArtistIdTag)
                     || TryGetSanitizedAdditionalFields(track, "MusicBrainz Album Artist Id", out musicBrainzReleaseArtistIdTag))
                    && !string.IsNullOrEmpty(musicBrainzReleaseArtistIdTag))
                {
                    string? id = GetFirstMusicBrainzId(musicBrainzReleaseArtistIdTag, libraryOptions.UseCustomTagDelimiters, libraryOptions.GetCustomTagDelimiters(), libraryOptions.DelimiterWhitelist);
                    audio.TrySetProviderId(MetadataProvider.MusicBrainzAlbumArtist, id);
                }
            }

            if (options.ReplaceAllMetadata || !audio.TryGetProviderId(MetadataProvider.MusicBrainzAlbum, out _))
            {
                if ((TryGetSanitizedAdditionalFields(track, "MUSICBRAINZ_ALBUMID", out string? musicBrainzReleaseIdTag)
                     || TryGetSanitizedAdditionalFields(track, "MusicBrainz Album Id", out musicBrainzReleaseIdTag))
                    && !string.IsNullOrEmpty(musicBrainzReleaseIdTag))
                {
                    string? id = GetFirstMusicBrainzId(musicBrainzReleaseIdTag, libraryOptions.UseCustomTagDelimiters, libraryOptions.GetCustomTagDelimiters(), libraryOptions.DelimiterWhitelist);
                    audio.TrySetProviderId(MetadataProvider.MusicBrainzAlbum, id);
                }
            }

            if (options.ReplaceAllMetadata || !audio.TryGetProviderId(MetadataProvider.MusicBrainzReleaseGroup, out _))
            {
                if ((TryGetSanitizedAdditionalFields(track, "MUSICBRAINZ_RELEASEGROUPID", out string? musicBrainzReleaseGroupIdTag)
                     || TryGetSanitizedAdditionalFields(track, "MusicBrainz Release Group Id", out musicBrainzReleaseGroupIdTag))
                    && !string.IsNullOrEmpty(musicBrainzReleaseGroupIdTag))
                {
                    string? id = GetFirstMusicBrainzId(musicBrainzReleaseGroupIdTag, libraryOptions.UseCustomTagDelimiters, libraryOptions.GetCustomTagDelimiters(), libraryOptions.DelimiterWhitelist);
                    audio.TrySetProviderId(MetadataProvider.MusicBrainzReleaseGroup, id);
                }
            }

            if (options.ReplaceAllMetadata || !audio.TryGetProviderId(MetadataProvider.MusicBrainzTrack, out _))
            {
                if ((TryGetSanitizedAdditionalFields(track, "MUSICBRAINZ_RELEASETRACKID", out string? trackMbId)
                     || TryGetSanitizedAdditionalFields(track, "MusicBrainz Release Track Id", out trackMbId))
                    && !string.IsNullOrEmpty(trackMbId))
                {
                    string? id = GetFirstMusicBrainzId(trackMbId, libraryOptions.UseCustomTagDelimiters, libraryOptions.GetCustomTagDelimiters(), libraryOptions.DelimiterWhitelist);
                    audio.TrySetProviderId(MetadataProvider.MusicBrainzTrack, id);
                }
            }

            if (options.ReplaceAllMetadata || !audio.TryGetProviderId(MetadataProvider.MusicBrainzRecording, out _))
            {
                if ((TryGetSanitizedAdditionalFields(track, "MUSICBRAINZ_TRACKID", out string? recordingMbId)
                     || TryGetSanitizedAdditionalFields(track, "MusicBrainz Track Id", out recordingMbId))
                    && !string.IsNullOrEmpty(recordingMbId))
                {
                    audio.TrySetProviderId(MetadataProvider.MusicBrainzRecording, recordingMbId);
                }
                else if (TryGetSanitizedUFIDFields(track, out string? owner, out string? identifier) && !string.IsNullOrEmpty(owner) && !string.IsNullOrEmpty(identifier))
                {
                    // If tagged with MB Picard, the format is 'http://musicbrainz.org\0<recording MBID>'
                    if (owner.Contains("musicbrainz.org", StringComparison.OrdinalIgnoreCase))
                    {
                        audio.TrySetProviderId(MetadataProvider.MusicBrainzRecording, identifier);
                    }
                }
            }

            // Save extracted lyrics if they exist,
            // and if the audio doesn't yet have lyrics.
            // ATL supports both SRT and LRC formats as synchronized lyrics, but we only want to save LRC format.
            List<LyricsInfo> supportedLyrics = track.Lyrics.Where(l => l.Format != LyricsInfo.LyricsFormat.SRT).ToList();
            LyricsInfo? candidateSynchronizedLyric = supportedLyrics.FirstOrDefault(l => l.Format is not LyricsInfo.LyricsFormat.UNSYNCHRONIZED and not LyricsInfo.LyricsFormat.OTHER && l.SynchronizedLyrics is not null);
            LyricsInfo? candidateUnsynchronizedLyric = supportedLyrics.FirstOrDefault(l => l.Format is LyricsInfo.LyricsFormat.UNSYNCHRONIZED or LyricsInfo.LyricsFormat.OTHER && l.UnsynchronizedLyrics is not null);
            string? lyrics = candidateSynchronizedLyric is not null ? candidateSynchronizedLyric.FormatSynch() : candidateUnsynchronizedLyric?.UnsynchronizedLyrics;
            if (!string.IsNullOrWhiteSpace(lyrics)
                && tryExtractEmbeddedLyrics)
            {
                await _lyricManager.SaveLyricAsync(audio, "lrc", lyrics).ConfigureAwait(false);
            }
        }

        private void AddExternalLyrics(
            Audio audio,
            List<MediaStream> currentStreams,
            MetadataRefreshOptions options)
        {
            int startIndex = currentStreams.Count == 0 ? 0 : (currentStreams.Select(i => i.Index).Max() + 1);
            IReadOnlyList<MediaStream> externalLyricFiles = _lyricResolver.GetExternalStreams(audio, startIndex, options.DirectoryService, false);

            audio.LyricFiles = externalLyricFiles.Select(i => i.Path).Distinct().ToArray();
            if (externalLyricFiles.Count > 0)
            {
                currentStreams.Add(externalLyricFiles[0]);
            }
        }

        private List<string> SplitWithCustomDelimiter(string val, char[] tagDelimiters, string[] whitelist)
        {
            List<string> items = new List<string>();
            string temp = val;
            foreach (string whitelistItem in whitelist)
            {
                if (string.IsNullOrWhiteSpace(whitelistItem))
                {
                    continue;
                }

                string originalTemp = temp;
                temp = temp.Replace(whitelistItem, string.Empty, StringComparison.OrdinalIgnoreCase);

                if (!string.Equals(temp, originalTemp, StringComparison.OrdinalIgnoreCase))
                {
                    items.Add(whitelistItem);
                }
            }

            IEnumerable<string> items2 = temp.Split(tagDelimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).DistinctNames();
            items.AddRange(items2);

            return items;
        }

        // MusicBrainz IDs are multi-value tags, so we need to split them
        // However, our current provider can only have one single ID, which means we need to pick the first one
        private string? GetFirstMusicBrainzId(string tag, bool useCustomTagDelimiters, char[] tagDelimiters, string[] whitelist)
        {
            string? val = tag.Split(InternalValueSeparator).FirstOrDefault();
            if (val is not null && useCustomTagDelimiters)
            {
                val = SplitWithCustomDelimiter(val, tagDelimiters, whitelist).FirstOrDefault();
            }

            return val;
        }

        private string? GetSanitizedStringTag(string? tag, string filePath)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return null;
            }

            string result = tag.TruncateAtNull();
            if (result.Length != tag.Length)
            {
                _logger.LogWarning("Audio file {File} contains a null character in its tag, but this is not allowed by its tagging standard. All characters after the null char will be discarded. Please fix your file", filePath);
            }

            return result;
        }

        private bool TryGetSanitizedAdditionalFields(Track track, string field, out string? value)
        {
            bool hasField = TryGetAdditionalFieldWithFallback(track, field, out value);
            value = GetSanitizedStringTag(value, track.Path);
            return hasField;
        }

        private bool TryGetSanitizedUFIDFields(Track track, out string? owner, out string? identifier)
        {
            bool hasField = TryGetAdditionalFieldWithFallback(track, "UFID", out string? value);
            if (hasField && !string.IsNullOrEmpty(value))
            {
                string[] parts = value.Split('\0');
                if (parts.Length == 2)
                {
                    owner = GetSanitizedStringTag(parts[0], track.Path);
                    identifier = GetSanitizedStringTag(parts[1], track.Path);
                    return true;
                }
            }

            owner = null;
            identifier = null;
            return false;
        }

        // Build the explicit mka-style fallback key (e.g., ARTISTS -> track.artists, "MusicBrainz Artist Id" -> track.musicbrainz_artist_id)
        private static string GetMkaFallbackKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return key;
            }

            string normalized = key.Trim().Replace(' ', '_').ToLowerInvariant();
            return "track." + normalized;
        }

        // First try the normal key exactly; if missing, try the mka-style fallback key.
        private bool TryGetAdditionalFieldWithFallback(Track track, string key, out string? value)
        {
            // Prefer the normal key (as-is, case-sensitive)
            if (track.AdditionalFields.TryGetValue(key, out value))
            {
                return true;
            }

            // Fallback to mka-style: "track." + lower-case(original key)
            string fallbackKey = GetMkaFallbackKey(key);
            if (track.AdditionalFields.TryGetValue(fallbackKey, out value))
            {
                return true;
            }

            value = null;
            return false;
        }
    }
}
