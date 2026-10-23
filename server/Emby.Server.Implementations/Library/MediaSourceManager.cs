#nullable disable

#pragma warning disable CS1591

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AsyncKeyedLock;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Extensions;
using Jellyfin.Extensions.Json;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.Library
{
    public class MediaSourceManager : IMediaSourceManager, IDisposable
    {
        // Do not use a pipe here because Roku http requests to the server will fail, without any explicit error message.
        private const char LiveStreamIdDelimiter = '_';

        private readonly IServerApplicationHost _appHost;
        private readonly IItemRepository _itemRepo;
        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger<MediaSourceManager> _logger;
        private readonly IUserDataManager _userDataManager;
        private readonly IMediaEncoder _mediaEncoder;
        private readonly ILocalizationManager _localizationManager;
        private readonly IApplicationPaths _appPaths;
        private readonly IDirectoryService _directoryService;
        private readonly IMediaStreamRepository _mediaStreamRepository;
        private readonly IMediaAttachmentRepository _mediaAttachmentRepository;
        private readonly ConcurrentDictionary<string, ILiveStream> _openStreams = new ConcurrentDictionary<string, ILiveStream>(StringComparer.OrdinalIgnoreCase);
        private readonly AsyncNonKeyedLocker _liveStreamLocker = new(1);
        private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Options;

        private IMediaSourceProvider[] _providers;

        public MediaSourceManager(
            IServerApplicationHost appHost,
            IItemRepository itemRepo,
            IApplicationPaths applicationPaths,
            ILocalizationManager localizationManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            ILogger<MediaSourceManager> logger,
            IFileSystem fileSystem,
            IUserDataManager userDataManager,
            IMediaEncoder mediaEncoder,
            IDirectoryService directoryService,
            IMediaStreamRepository mediaStreamRepository,
            IMediaAttachmentRepository mediaAttachmentRepository)
        {
            _appHost = appHost;
            _itemRepo = itemRepo;
            _userManager = userManager;
            _libraryManager = libraryManager;
            _logger = logger;
            _fileSystem = fileSystem;
            _userDataManager = userDataManager;
            _mediaEncoder = mediaEncoder;
            _localizationManager = localizationManager;
            _appPaths = applicationPaths;
            _directoryService = directoryService;
            _mediaStreamRepository = mediaStreamRepository;
            _mediaAttachmentRepository = mediaAttachmentRepository;
        }

        public void AddParts(IEnumerable<IMediaSourceProvider> providers)
        {
            _providers = providers.ToArray();
        }

        public IReadOnlyList<MediaStream> GetMediaStreams(MediaStreamQuery query)
        {
            IReadOnlyList<MediaStream> list = _mediaStreamRepository.GetMediaStreams(query);

            foreach (MediaStream stream in list)
            {
                stream.SupportsExternalStream = StreamSupportsExternalStream(stream);
            }

            return list;
        }

        private static bool StreamSupportsExternalStream(MediaStream stream)
        {
            if (stream.IsExternal)
            {
                return true;
            }

            if (stream.IsTextSubtitleStream)
            {
                return true;
            }

            if (stream.IsPgsSubtitleStream)
            {
                return true;
            }

            return false;
        }

        public IReadOnlyList<MediaStream> GetMediaStreams(Guid itemId)
        {
            IReadOnlyList<MediaStream> list = GetMediaStreams(new MediaStreamQuery
            {
                ItemId = itemId
            });

            return GetMediaStreamsForItem(list);
        }

        private IReadOnlyList<MediaStream> GetMediaStreamsForItem(IReadOnlyList<MediaStream> streams)
        {
            foreach (MediaStream stream in streams)
            {
                if (stream.Type == MediaStreamType.Subtitle)
                {
                    stream.SupportsExternalStream = StreamSupportsExternalStream(stream);
                }
            }

            return streams;
        }

        /// <inheritdoc />
        public IReadOnlyList<MediaAttachment> GetMediaAttachments(MediaAttachmentQuery query)
        {
            return _mediaAttachmentRepository.GetMediaAttachments(query);
        }

        /// <inheritdoc />
        public IReadOnlyList<MediaAttachment> GetMediaAttachments(Guid itemId)
        {
            return GetMediaAttachments(new MediaAttachmentQuery
            {
                ItemId = itemId
            });
        }

        public async Task<IReadOnlyList<MediaSourceInfo>> GetPlaybackMediaSources(BaseItem item, User user, bool allowMediaProbe, bool enablePathSubstitution, CancellationToken cancellationToken)
        {
            IReadOnlyList<MediaSourceInfo> mediaSources = GetStaticMediaSources(item, enablePathSubstitution, user);

            // If file is strm or main media stream is missing, force a metadata refresh with remote probing
            if (allowMediaProbe && mediaSources[0].Type != MediaSourceType.Placeholder
                && (item.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase)
                    || (item.MediaType == MediaType.Video && mediaSources[0].MediaStreams.All(i => i.Type != MediaStreamType.Video))
                    || (item.MediaType == MediaType.Audio && mediaSources[0].MediaStreams.All(i => i.Type != MediaStreamType.Audio))))
            {
                await item.RefreshMetadata(
                    new MetadataRefreshOptions(_directoryService)
                    {
                        EnableRemoteContentProbe = true,
                        MetadataRefreshMode = MetadataRefreshMode.FullRefresh
                    },
                    cancellationToken).ConfigureAwait(false);

                mediaSources = GetStaticMediaSources(item, enablePathSubstitution, user);
            }

            IEnumerable<MediaSourceInfo> dynamicMediaSources = await GetDynamicMediaSources(item, cancellationToken).ConfigureAwait(false);

            List<MediaSourceInfo> list = new List<MediaSourceInfo>();

            list.AddRange(mediaSources);

            foreach (MediaSourceInfo source in dynamicMediaSources)
            {
                // Validate that this is actually possible
                if (source.SupportsDirectStream)
                {
                    source.SupportsDirectStream = SupportsDirectStream(source.Path, source.Protocol);
                }

                if (user is not null)
                {
                    SetDefaultAudioAndSubtitleStreamIndices(item, source, user);

                    if (item.MediaType == MediaType.Audio)
                    {
                        source.SupportsTranscoding = user.HasPermission(PermissionKind.EnableAudioPlaybackTranscoding);
                    }
                    else if (item.MediaType == MediaType.Video)
                    {
                        source.SupportsTranscoding = user.HasPermission(PermissionKind.EnableVideoPlaybackTranscoding);
                        source.SupportsDirectStream = user.HasPermission(PermissionKind.EnablePlaybackRemuxing);
                    }
                }

                list.Add(source);
            }

            return SortMediaSources(list).ToArray();
        }

        /// <inheritdoc />>
        public MediaProtocol GetPathProtocol(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return MediaProtocol.File;
            }

            if (path.StartsWith("Rtsp", StringComparison.OrdinalIgnoreCase))
            {
                return MediaProtocol.Rtsp;
            }

            if (path.StartsWith("Rtmp", StringComparison.OrdinalIgnoreCase))
            {
                return MediaProtocol.Rtmp;
            }

            if (path.StartsWith("Http", StringComparison.OrdinalIgnoreCase))
            {
                return MediaProtocol.Http;
            }

            if (path.StartsWith("rtp", StringComparison.OrdinalIgnoreCase))
            {
                return MediaProtocol.Rtp;
            }

            if (path.StartsWith("ftp", StringComparison.OrdinalIgnoreCase))
            {
                return MediaProtocol.Ftp;
            }

            if (path.StartsWith("udp", StringComparison.OrdinalIgnoreCase))
            {
                return MediaProtocol.Udp;
            }

            return _fileSystem.IsPathFile(path) ? MediaProtocol.File : MediaProtocol.Http;
        }

        public bool SupportsDirectStream(string path, MediaProtocol protocol)
        {
            if (protocol == MediaProtocol.File)
            {
                return true;
            }

            if (protocol == MediaProtocol.Http)
            {
                if (path is not null)
                {
                    if (path.Contains(".m3u", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    return true;
                }
            }

            return false;
        }

        private async Task<IEnumerable<MediaSourceInfo>> GetDynamicMediaSources(BaseItem item, CancellationToken cancellationToken)
        {
            IEnumerable<Task<IEnumerable<MediaSourceInfo>>> tasks = _providers.Select(i => GetDynamicMediaSources(item, i, cancellationToken));
            IEnumerable<MediaSourceInfo>[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

            return results.SelectMany(i => i);
        }

        private async Task<IEnumerable<MediaSourceInfo>> GetDynamicMediaSources(BaseItem item, IMediaSourceProvider provider, CancellationToken cancellationToken)
        {
            try
            {
                IEnumerable<MediaSourceInfo> sources = await provider.GetMediaSources(item, cancellationToken).ConfigureAwait(false);
                List<MediaSourceInfo> list = sources.ToList();

                foreach (MediaSourceInfo mediaSource in list)
                {
                    mediaSource.InferTotalBitrate();

                    SetKeyProperties(provider, mediaSource);
                }

                return list;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting media sources");
                return [];
            }
        }

        private static void SetKeyProperties(IMediaSourceProvider provider, MediaSourceInfo mediaSource)
        {
            string prefix = provider.GetType().FullName.GetMD5().ToString("N", CultureInfo.InvariantCulture) + LiveStreamIdDelimiter;

            if (!string.IsNullOrEmpty(mediaSource.OpenToken) && !mediaSource.OpenToken.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                mediaSource.OpenToken = prefix + mediaSource.OpenToken;
            }

            if (!string.IsNullOrEmpty(mediaSource.LiveStreamId) && !mediaSource.LiveStreamId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                mediaSource.LiveStreamId = prefix + mediaSource.LiveStreamId;
            }
        }

        public async Task<MediaSourceInfo> GetMediaSource(BaseItem item, string mediaSourceId, string liveStreamId, bool enablePathSubstitution, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(liveStreamId))
            {
                return await GetLiveStream(liveStreamId, cancellationToken).ConfigureAwait(false);
            }

            IReadOnlyList<MediaSourceInfo> sources = await GetPlaybackMediaSources(item, null, false, enablePathSubstitution, cancellationToken).ConfigureAwait(false);

            return sources.FirstOrDefault(i => string.Equals(i.Id, mediaSourceId, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<MediaSourceInfo> GetStaticMediaSources(BaseItem item, bool enablePathSubstitution, User user = null)
        {
            ArgumentNullException.ThrowIfNull(item);

            IHasMediaSources hasMediaSources = (IHasMediaSources)item;

            IReadOnlyList<MediaSourceInfo> sources = hasMediaSources.GetMediaSources(enablePathSubstitution);

            if (user is not null)
            {
                foreach (MediaSourceInfo source in sources)
                {
                    SetDefaultAudioAndSubtitleStreamIndices(item, source, user);

                    if (item.MediaType == MediaType.Audio)
                    {
                        source.SupportsTranscoding = user.HasPermission(PermissionKind.EnableAudioPlaybackTranscoding);
                    }
                    else if (item.MediaType == MediaType.Video)
                    {
                        source.SupportsTranscoding = user.HasPermission(PermissionKind.EnableVideoPlaybackTranscoding);
                        source.SupportsDirectStream = user.HasPermission(PermissionKind.EnablePlaybackRemuxing);
                    }
                }
            }

            return sources;
        }

        private IReadOnlyList<string> NormalizeLanguage(string language)
        {
            if (string.IsNullOrEmpty(language))
            {
                return [];
            }

            CultureDto culture = _localizationManager.FindLanguageInfo(language);
            if (culture is not null)
            {
                return culture.Name.Contains('-', StringComparison.OrdinalIgnoreCase) ? [culture.Name] : culture.ThreeLetterISOLanguageNames;
            }

            return [language];
        }

        private void SetDefaultSubtitleStreamIndex(MediaSourceInfo source, UserItemData userData, User user, bool allowRememberingSelection)
        {
            if (userData is not null
                && userData.SubtitleStreamIndex.HasValue
                && user.RememberSubtitleSelections
                && user.SubtitleMode != SubtitlePlaybackMode.None
                && allowRememberingSelection)
            {
                int index = userData.SubtitleStreamIndex.Value;
                // Make sure the saved index is still valid
                if (index == -1 || source.MediaStreams.Any(i => i.Type == MediaStreamType.Subtitle && i.Index == index))
                {
                    source.DefaultSubtitleStreamIndex = index;
                    return;
                }
            }

            IReadOnlyList<string> preferredSubs = NormalizeLanguage(user.SubtitleLanguagePreference);

            int? defaultAudioIndex = source.DefaultAudioStreamIndex;
            string audioLanguage = defaultAudioIndex is null
                ? null
                : source.MediaStreams.Where(i => i.Type == MediaStreamType.Audio && i.Index == defaultAudioIndex).Select(i => i.Language).FirstOrDefault();

            source.DefaultSubtitleStreamIndex = MediaStreamSelector.GetDefaultSubtitleStreamIndex(
                source.MediaStreams,
                preferredSubs,
                user.SubtitleMode,
                audioLanguage);

            MediaStreamSelector.SetSubtitleStreamScores(source.MediaStreams, preferredSubs, user.SubtitleMode, audioLanguage);
        }

        private void SetDefaultAudioStreamIndex(MediaSourceInfo source, UserItemData userData, User user, bool allowRememberingSelection)
        {
            if (userData is not null && userData.AudioStreamIndex.HasValue && user.RememberAudioSelections && allowRememberingSelection)
            {
                int index = userData.AudioStreamIndex.Value;
                // Make sure the saved index is still valid
                if (source.MediaStreams.Any(i => i.Type == MediaStreamType.Audio && i.Index == index))
                {
                    source.DefaultAudioStreamIndex = index;
                    source.DefaultAudioIndexSource = AudioIndexSource.User;
                    return;
                }
            }

            IReadOnlyList<string> preferredAudio = NormalizeLanguage(user.AudioLanguagePreference);

            source.DefaultAudioStreamIndex = MediaStreamSelector.GetDefaultAudioStreamIndex(source.MediaStreams, preferredAudio, user.PlayDefaultAudioTrack);
            if (user.PlayDefaultAudioTrack)
            {
                source.DefaultAudioIndexSource |= AudioIndexSource.Default;
            }

            if (preferredAudio.Count > 0)
            {
                source.DefaultAudioIndexSource |= AudioIndexSource.Language;
            }
        }

        public void SetDefaultAudioAndSubtitleStreamIndices(BaseItem item, MediaSourceInfo source, User user)
        {
            // Item would only be null if the app didn't supply ItemId as part of the live stream open request
            MediaType mediaType = item?.MediaType ?? MediaType.Video;

            if (mediaType == MediaType.Video)
            {
                UserItemData userData = item is null ? null : _userDataManager.GetUserData(user, item);

                bool allowRememberingSelection = item is null || item.EnableRememberingTrackSelections;

                SetDefaultAudioStreamIndex(source, userData, user, allowRememberingSelection);
                SetDefaultSubtitleStreamIndex(source, userData, user, allowRememberingSelection);
            }
            else if (mediaType == MediaType.Audio)
            {
                MediaStream audio = source.MediaStreams.FirstOrDefault(i => i.Type == MediaStreamType.Audio);

                if (audio is not null)
                {
                    source.DefaultAudioStreamIndex = audio.Index;
                }
            }
        }

        private static IEnumerable<MediaSourceInfo> SortMediaSources(IEnumerable<MediaSourceInfo> sources)
        {
            return sources.OrderBy(i =>
            {
                if (i.VideoType.HasValue && i.VideoType.Value == VideoType.VideoFile)
                {
                    return 0;
                }

                return 1;
            }).ThenBy(i => i.Video3DFormat.HasValue ? 1 : 0)
            .ThenByDescending(i =>
            {
                MediaStream stream = i.VideoStream;

                return stream?.Width ?? 0;
            })
            .Where(i => i.Type != MediaSourceType.Placeholder);
        }

        public async Task<Tuple<LiveStreamResponse, IDirectStreamProvider>> OpenLiveStreamInternal(LiveStreamRequest request, CancellationToken cancellationToken)
        {
            MediaSourceInfo mediaSource;
            ILiveStream liveStream;

            using (await _liveStreamLocker.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                (IMediaSourceProvider provider, string keyId) = GetProvider(request.OpenToken);

                List<ILiveStream> currentLiveStreams = _openStreams.Values.ToList();

                liveStream = await provider.OpenMediaSource(keyId, currentLiveStreams, cancellationToken).ConfigureAwait(false);

                mediaSource = liveStream.MediaSource;

                // Validate that this is actually possible
                if (mediaSource.SupportsDirectStream)
                {
                    mediaSource.SupportsDirectStream = SupportsDirectStream(mediaSource.Path, mediaSource.Protocol);
                }

                SetKeyProperties(provider, mediaSource);

                _openStreams[mediaSource.LiveStreamId] = liveStream;
            }

            try
            {
                if (mediaSource.MediaStreams.Any(i => i.Index != -1) || !mediaSource.SupportsProbing)
                {
                    AddMediaInfo(mediaSource);
                }
                else
                {
                    // hack - these two values were taken from LiveTVMediaSourceProvider
                    string cacheKey = request.OpenToken;

                    await new LiveStreamHelper(_mediaEncoder, _logger, _appPaths)
                        .AddMediaInfoWithProbe(mediaSource, false, cacheKey, true, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error probing live tv stream");
                AddMediaInfo(mediaSource);
            }

            // TODO: @bond Fix
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(mediaSource, _jsonOptions);
            _logger.LogInformation("Live stream opened: {@MediaSource}", mediaSource);
            MediaSourceInfo clone = JsonSerializer.Deserialize<MediaSourceInfo>(json, _jsonOptions);

            if (!request.UserId.IsEmpty())
            {
                User user = _userManager.GetUserById(request.UserId);
                BaseItem item = request.ItemId.IsEmpty()
                    ? null
                    : _libraryManager.GetItemById(request.ItemId);
                SetDefaultAudioAndSubtitleStreamIndices(item, clone, user);
            }

            return new Tuple<LiveStreamResponse, IDirectStreamProvider>(new LiveStreamResponse(clone), liveStream as IDirectStreamProvider);
        }

        private static void AddMediaInfo(MediaSourceInfo mediaSource)
        {
            mediaSource.DefaultSubtitleStreamIndex = null;

            // Null this out so that it will be treated like a live stream
            if (mediaSource.IsInfiniteStream)
            {
                mediaSource.RunTimeTicks = null;
            }

            MediaStream audioStream = mediaSource.MediaStreams.FirstOrDefault(i => i.Type == MediaStreamType.Audio);

            if (audioStream is null || audioStream.Index == -1)
            {
                mediaSource.DefaultAudioStreamIndex = null;
            }
            else
            {
                mediaSource.DefaultAudioStreamIndex = audioStream.Index;
            }

            MediaStream videoStream = mediaSource.MediaStreams.FirstOrDefault(i => i.Type == MediaStreamType.Video);
            if (videoStream is not null)
            {
                if (!videoStream.BitRate.HasValue)
                {
                    int width = videoStream.Width ?? 1920;

                    if (width >= 3000)
                    {
                        videoStream.BitRate = 30000000;
                    }
                    else if (width >= 1900)
                    {
                        videoStream.BitRate = 20000000;
                    }
                    else if (width >= 1200)
                    {
                        videoStream.BitRate = 8000000;
                    }
                    else if (width >= 700)
                    {
                        videoStream.BitRate = 2000000;
                    }
                }
            }

            // Try to estimate this
            mediaSource.InferTotalBitrate();
        }

        public async Task<LiveStreamResponse> OpenLiveStream(LiveStreamRequest request, CancellationToken cancellationToken)
        {
            Tuple<LiveStreamResponse, IDirectStreamProvider> result = await OpenLiveStreamInternal(request, cancellationToken).ConfigureAwait(false);
            return result.Item1;
        }

        public async Task<MediaSourceInfo> GetLiveStreamMediaInfo(string id, CancellationToken cancellationToken)
        {
            // TODO probably shouldn't throw here but it is kept for "backwards compatibility"
            ILiveStream liveStreamInfo = GetLiveStreamInfo(id) ?? throw new ResourceNotFoundException();

            MediaSourceInfo mediaSource = liveStreamInfo.MediaSource;

            if (liveStreamInfo is IDirectStreamProvider)
            {
                MediaInfo info = await _mediaEncoder.GetMediaInfo(
                    new MediaInfoRequest
                    {
                        MediaSource = mediaSource,
                        ExtractChapters = false,
                        MediaType = DlnaProfileType.Video
                    },
                    cancellationToken).ConfigureAwait(false);

                mediaSource.MediaStreams = info.MediaStreams;
                mediaSource.Container = info.Container;
                mediaSource.Bitrate = info.Bitrate;
            }

            return mediaSource;
        }

        public async Task AddMediaInfoWithProbe(MediaSourceInfo mediaSource, bool isAudio, string cacheKey, bool addProbeDelay, bool isLiveStream, CancellationToken cancellationToken)
        {
            long? originalRuntime = mediaSource.RunTimeTicks;

            DateTime now = DateTime.UtcNow;

            MediaInfo mediaInfo = null;
            string cacheFilePath = string.IsNullOrEmpty(cacheKey) ? null : Path.Combine(_appPaths.CachePath, "mediainfo", cacheKey.GetMD5().ToString("N", CultureInfo.InvariantCulture) + ".json");

            if (!string.IsNullOrEmpty(cacheKey))
            {
                FileStream jsonStream = AsyncFile.OpenRead(cacheFilePath);
                try
                {
                    mediaInfo = await JsonSerializer.DeserializeAsync<MediaInfo>(jsonStream, _jsonOptions, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error parsing cached media info.");
                }
                finally
                {
                    await jsonStream.DisposeAsync().ConfigureAwait(false);
                }
            }

            if (mediaInfo is null)
            {
                if (addProbeDelay)
                {
                    int delayMs = mediaSource.AnalyzeDurationMs ?? 0;
                    delayMs = Math.Max(3000, delayMs);
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }

                if (isLiveStream)
                {
                    mediaSource.AnalyzeDurationMs = 3000;
                }

                mediaInfo = await _mediaEncoder.GetMediaInfo(
                    new MediaInfoRequest
                    {
                        MediaSource = mediaSource,
                        MediaType = isAudio ? DlnaProfileType.Audio : DlnaProfileType.Video,
                        ExtractChapters = false
                    },
                    cancellationToken).ConfigureAwait(false);

                if (cacheFilePath is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cacheFilePath));
                    FileStream createStream = AsyncFile.Create(cacheFilePath);
                    await using (createStream.ConfigureAwait(false))
                    {
                        await JsonSerializer.SerializeAsync(createStream, mediaInfo, _jsonOptions, cancellationToken).ConfigureAwait(false);
                    }

                    // _logger.LogDebug("Saved media info to {0}", cacheFilePath);
                }
            }

            IReadOnlyList<MediaStream> mediaStreams = mediaInfo.MediaStreams;

            if (isLiveStream && !string.IsNullOrEmpty(cacheKey))
            {
                List<MediaStream> newList = new List<MediaStream>();
                newList.AddRange(mediaStreams.Where(i => i.Type == MediaStreamType.Video).Take(1));
                newList.AddRange(mediaStreams.Where(i => i.Type == MediaStreamType.Audio).Take(1));

                foreach (MediaStream stream in newList)
                {
                    stream.Index = -1;
                    stream.Language = null;
                }

                mediaStreams = newList;
            }

            _logger.LogInformation("Live tv media info probe took {0} seconds", (DateTime.UtcNow - now).TotalSeconds.ToString(CultureInfo.InvariantCulture));

            mediaSource.Bitrate = mediaInfo.Bitrate;
            mediaSource.Container = mediaInfo.Container;
            mediaSource.Formats = mediaInfo.Formats;
            mediaSource.MediaStreams = mediaStreams;
            mediaSource.RunTimeTicks = mediaInfo.RunTimeTicks;
            mediaSource.Size = mediaInfo.Size;
            mediaSource.Timestamp = mediaInfo.Timestamp;
            mediaSource.Video3DFormat = mediaInfo.Video3DFormat;
            mediaSource.VideoType = mediaInfo.VideoType;

            mediaSource.DefaultSubtitleStreamIndex = null;

            if (isLiveStream)
            {
                // Null this out so that it will be treated like a live stream
                if (!originalRuntime.HasValue)
                {
                    mediaSource.RunTimeTicks = null;
                }
            }

            MediaStream audioStream = mediaStreams.FirstOrDefault(i => i.Type == MediaStreamType.Audio);

            if (audioStream is null || audioStream.Index == -1)
            {
                mediaSource.DefaultAudioStreamIndex = null;
            }
            else
            {
                mediaSource.DefaultAudioStreamIndex = audioStream.Index;
            }

            MediaStream videoStream = mediaStreams.FirstOrDefault(i => i.Type == MediaStreamType.Video);
            if (videoStream is not null)
            {
                if (!videoStream.BitRate.HasValue)
                {
                    int width = videoStream.Width ?? 1920;

                    if (width >= 3000)
                    {
                        videoStream.BitRate = 30000000;
                    }
                    else if (width >= 1900)
                    {
                        videoStream.BitRate = 20000000;
                    }
                    else if (width >= 1200)
                    {
                        videoStream.BitRate = 8000000;
                    }
                    else if (width >= 700)
                    {
                        videoStream.BitRate = 2000000;
                    }
                }

                // This is coming up false and preventing stream copy
                videoStream.IsAVC = null;
            }

            if (isLiveStream)
            {
                mediaSource.AnalyzeDurationMs = 3000;
            }

            // Try to estimate this
            mediaSource.InferTotalBitrate(true);
        }

        public Task<Tuple<MediaSourceInfo, IDirectStreamProvider>> GetLiveStreamWithDirectStreamProvider(string id, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(id);

            ILiveStream info = GetLiveStreamInfo(id);
            if (info is null)
            {
                return Task.FromResult<Tuple<MediaSourceInfo, IDirectStreamProvider>>(new Tuple<MediaSourceInfo, IDirectStreamProvider>(null, null));
            }

            return Task.FromResult<Tuple<MediaSourceInfo, IDirectStreamProvider>>(new Tuple<MediaSourceInfo, IDirectStreamProvider>(info.MediaSource, info as IDirectStreamProvider));
        }

        public ILiveStream GetLiveStreamInfo(string id)
        {
            ArgumentException.ThrowIfNullOrEmpty(id);

            if (_openStreams.TryGetValue(id, out ILiveStream info))
            {
                return info;
            }

            return null;
        }

        /// <inheritdoc />
        public ILiveStream GetLiveStreamInfoByUniqueId(string uniqueId)
        {
            return _openStreams.Values.FirstOrDefault(stream => string.Equals(uniqueId, stream?.UniqueId, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<MediaSourceInfo> GetLiveStream(string id, CancellationToken cancellationToken)
        {
            Tuple<MediaSourceInfo, IDirectStreamProvider> result = await GetLiveStreamWithDirectStreamProvider(id, cancellationToken).ConfigureAwait(false);
            return result.Item1;
        }

        public async Task<IReadOnlyList<MediaSourceInfo>> GetRecordingStreamMediaSources(ActiveRecordingInfo info, CancellationToken cancellationToken)
        {
            MediaSourceInfo stream = new MediaSourceInfo
            {
                EncoderPath = _appHost.GetApiUrlForLocalAccess() + "/LiveTv/LiveRecordings/" + info.Id + "/stream",
                EncoderProtocol = MediaProtocol.Http,
                Path = info.Path,
                Protocol = MediaProtocol.File,
                Id = info.Id,
                SupportsDirectPlay = false,
                SupportsDirectStream = true,
                SupportsTranscoding = true,
                IsInfiniteStream = true,
                RequiresOpening = false,
                RequiresClosing = false,
                BufferMs = 0,
                IgnoreDts = true,
                IgnoreIndex = true
            };

            await new LiveStreamHelper(_mediaEncoder, _logger, _appPaths)
                .AddMediaInfoWithProbe(stream, false, false, cancellationToken).ConfigureAwait(false);

            return [stream];
        }

        public async Task CloseLiveStream(string id)
        {
            ArgumentException.ThrowIfNullOrEmpty(id);

            using (await _liveStreamLocker.LockAsync().ConfigureAwait(false))
            {
                if (_openStreams.TryGetValue(id, out ILiveStream liveStream))
                {
                    liveStream.ConsumerCount--;

                    _logger.LogInformation("Live stream {0} consumer count is now {1}", liveStream.OriginalStreamId, liveStream.ConsumerCount);

                    if (liveStream.ConsumerCount <= 0)
                    {
                        _openStreams.TryRemove(id, out _);

                        _logger.LogInformation("Closing live stream {0}", id);

                        await liveStream.Close().ConfigureAwait(false);
                        _logger.LogInformation("Live stream {0} closed successfully", id);
                    }
                }
            }
        }

        private (IMediaSourceProvider MediaSourceProvider, string KeyId) GetProvider(string key)
        {
            ArgumentException.ThrowIfNullOrEmpty(key);

            string[] keys = key.Split(LiveStreamIdDelimiter, 2);

            IMediaSourceProvider provider = _providers.FirstOrDefault(i => string.Equals(i.GetType().FullName.GetMD5().ToString("N", CultureInfo.InvariantCulture), keys[0], StringComparison.OrdinalIgnoreCase));

            int splitIndex = key.IndexOf(LiveStreamIdDelimiter, StringComparison.Ordinal);
            string keyId = key.Substring(splitIndex + 1);

            return (provider, keyId);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="dispose"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool dispose)
        {
            if (dispose)
            {
                foreach (string key in _openStreams.Keys.ToList())
                {
                    CloseLiveStream(key).GetAwaiter().GetResult();
                }

                _liveStreamLocker.Dispose();
            }
        }
    }
}
