using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using Jellyfin.LiveTv.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.LiveTv;
using Microsoft.Extensions.Logging;

namespace Jellyfin.LiveTv.Guide;

/// <inheritdoc />
public class GuideManager : IGuideManager
{
    private const int MaxGuideDays = 14;
    private const string EtagKey = "ProgramEtag";
    private const string ExternalServiceTag = "ExternalServiceId";

    private static readonly ParallelOptions _cacheParallelOptions = new() { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 10) };

    private readonly ILogger<GuideManager> _logger;
    private readonly IConfigurationManager _config;
    private readonly IFileSystem _fileSystem;
    private readonly IItemRepository _itemRepo;
    private readonly ILibraryManager _libraryManager;
    private readonly ILiveTvManager _liveTvManager;
    private readonly ITunerHostManager _tunerHostManager;
    private readonly IRecordingsManager _recordingsManager;
    private readonly LiveTvDtoService _tvDtoService;

    /// <summary>
    /// Amount of days images are pre-cached from external sources.
    /// </summary>
    public const int MaxCacheDays = 2;

    /// <summary>
    /// Initializes a new instance of the <see cref="GuideManager"/> class.
    /// </summary>
    /// <param name="logger">The <see cref="ILogger{TCategoryName}"/>.</param>
    /// <param name="config">The <see cref="IConfigurationManager"/>.</param>
    /// <param name="fileSystem">The <see cref="IFileSystem"/>.</param>
    /// <param name="itemRepo">The <see cref="IItemRepository"/>.</param>
    /// <param name="libraryManager">The <see cref="ILibraryManager"/>.</param>
    /// <param name="liveTvManager">The <see cref="ILiveTvManager"/>.</param>
    /// <param name="tunerHostManager">The <see cref="ITunerHostManager"/>.</param>
    /// <param name="recordingsManager">The <see cref="IRecordingsManager"/>.</param>
    /// <param name="tvDtoService">The <see cref="LiveTvDtoService"/>.</param>
    public GuideManager(
        ILogger<GuideManager> logger,
        IConfigurationManager config,
        IFileSystem fileSystem,
        IItemRepository itemRepo,
        ILibraryManager libraryManager,
        ILiveTvManager liveTvManager,
        ITunerHostManager tunerHostManager,
        IRecordingsManager recordingsManager,
        LiveTvDtoService tvDtoService)
    {
        _logger = logger;
        _config = config;
        _fileSystem = fileSystem;
        _itemRepo = itemRepo;
        _libraryManager = libraryManager;
        _liveTvManager = liveTvManager;
        _tunerHostManager = tunerHostManager;
        _recordingsManager = recordingsManager;
        _tvDtoService = tvDtoService;
    }

    /// <inheritdoc />
    public GuideInfo GetGuideInfo()
    {
        DateTime startDate = DateTime.UtcNow;
        DateTime endDate = startDate.AddDays(GetGuideDays());

        return new GuideInfo
        {
            StartDate = startDate,
            EndDate = endDate
        };
    }

    /// <inheritdoc />
    public async Task RefreshGuide(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        await _recordingsManager.CreateRecordingFolders().ConfigureAwait(false);

        await _tunerHostManager.ScanForTunerDeviceChanges(cancellationToken).ConfigureAwait(false);

        int numComplete = 0;
        double progressPerService = _liveTvManager.Services.Count == 0
            ? 0
            : 1.0 / _liveTvManager.Services.Count;

        List<Guid> newChannelIdList = new List<Guid>();
        List<Guid> newProgramIdList = new List<Guid>();

        bool cleanDatabase = true;

        foreach (ILiveTvService? service in _liveTvManager.Services)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug("Refreshing guide from {Name}", service.Name);

            try
            {
                Progress<double> innerProgress = new Progress<double>(p => progress.Report(p * progressPerService));

                Tuple<List<Guid>, List<Guid>> idList = await RefreshChannelsInternal(service, innerProgress, cancellationToken).ConfigureAwait(false);

                newChannelIdList.AddRange(idList.Item1);
                newProgramIdList.AddRange(idList.Item2);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                cleanDatabase = false;
                _logger.LogError(ex, "Error refreshing channels for service");
            }

            numComplete++;
            double percent = numComplete;
            percent /= _liveTvManager.Services.Count;

            progress.Report(100 * percent);
        }

        if (cleanDatabase)
        {
            CleanDatabase(newChannelIdList.ToArray(), [BaseItemKind.LiveTvChannel], progress, cancellationToken);
            CleanDatabase(newProgramIdList.ToArray(), [BaseItemKind.LiveTvProgram], progress, cancellationToken);
        }

        DefaultLiveTvService? coreService = _liveTvManager.Services.OfType<DefaultLiveTvService>().FirstOrDefault();
        if (coreService is not null)
        {
            await coreService.RefreshSeriesTimers(cancellationToken).ConfigureAwait(false);
            await coreService.RefreshTimers(cancellationToken).ConfigureAwait(false);
        }

        progress.Report(100);
    }

    private double GetGuideDays()
    {
        LiveTvOptions config = _config.GetLiveTvConfiguration();

        return config.GuideDays.HasValue
            ? Math.Clamp(config.GuideDays.Value, 1, MaxGuideDays)
            : 7;
    }

    private async Task<Tuple<List<Guid>, List<Guid>>> RefreshChannelsInternal(ILiveTvService service, IProgress<double> progress, CancellationToken cancellationToken)
    {
        progress.Report(10);

        List<Tuple<string, ChannelInfo>> allChannelsList = (await service.GetChannelsAsync(cancellationToken).ConfigureAwait(false))
            .Select(i => new Tuple<string, ChannelInfo>(service.Name, i))
            .ToList();

        List<LiveTvChannel> list = new List<LiveTvChannel>();

        int numComplete = 0;
        Folder parentFolder = _liveTvManager.GetInternalLiveTvFolder(cancellationToken);

        foreach (Tuple<string, ChannelInfo> channelInfo in allChannelsList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                LiveTvChannel item = await GetChannel(channelInfo.Item2, channelInfo.Item1, parentFolder, cancellationToken).ConfigureAwait(false);

                list.Add(item);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting channel information for {Name}", channelInfo.Item2.Name);
            }

            numComplete++;
            double percent = numComplete;
            percent /= allChannelsList.Count;

            progress.Report((5 * percent) + 10);
        }

        progress.Report(15);

        numComplete = 0;
        List<Guid> programIds = new List<Guid>();
        List<Guid> channels = new List<Guid>();

        double guideDays = GetGuideDays();

        _logger.LogInformation("Refreshing guide with {Days} days of guide data", guideDays);

        DateTime maxCacheDate = DateTime.UtcNow.AddDays(MaxCacheDays);
        foreach (LiveTvChannel currentChannel in list)
        {
            cancellationToken.ThrowIfCancellationRequested();
            channels.Add(currentChannel.Id);

            try
            {
                DateTime start = DateTime.UtcNow.AddHours(-1);
                DateTime end = start.AddDays(guideDays);

                bool isMovie = false;
                bool isSports = false;
                bool isNews = false;
                bool isKids = false;
                bool isSeries = false;

                List<ProgramInfo> channelPrograms = (await service.GetProgramsAsync(currentChannel.ExternalId, start, end, cancellationToken).ConfigureAwait(false)).ToList();

                Dictionary<Guid, LiveTvProgram> existingPrograms = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = [BaseItemKind.LiveTvProgram],
                    ChannelIds = [currentChannel.Id],
                    DtoOptions = new DtoOptions(true)
                }).Cast<LiveTvProgram>().ToDictionary(i => i.Id);

                List<LiveTvProgram> newPrograms = new List<LiveTvProgram>();
                List<LiveTvProgram> updatedPrograms = new List<LiveTvProgram>();

                foreach (ProgramInfo program in channelPrograms)
                {
                    (LiveTvProgram? programItem, bool isNew, bool isUpdated) = GetProgram(program, existingPrograms, currentChannel);
                    Guid id = programItem.Id;
                    if (isNew)
                    {
                        newPrograms.Add(programItem);
                    }
                    else if (isUpdated)
                    {
                        updatedPrograms.Add(programItem);
                    }

                    programIds.Add(programItem.Id);

                    isMovie |= program.IsMovie;
                    isSeries |= program.IsSeries;
                    isSports |= program.IsSports;
                    isNews |= program.IsNews;
                    isKids |= program.IsKids;
                }

                _logger.LogDebug(
                    "Channel {Name} has {NewCount} new programs and {UpdatedCount} updated programs",
                    currentChannel.Name,
                    newPrograms.Count,
                    updatedPrograms.Count);

                if (newPrograms.Count > 0)
                {
                    _libraryManager.CreateItems(newPrograms, currentChannel, cancellationToken);

                    await PreCacheImages(newPrograms, maxCacheDate).ConfigureAwait(false);
                }

                if (updatedPrograms.Count > 0)
                {
                    await _libraryManager.UpdateItemsAsync(
                        updatedPrograms,
                        currentChannel,
                        ItemUpdateType.MetadataImport,
                        cancellationToken).ConfigureAwait(false);

                    await PreCacheImages(updatedPrograms, maxCacheDate).ConfigureAwait(false);
                }

                currentChannel.IsMovie = isMovie;
                currentChannel.IsNews = isNews;
                currentChannel.IsSports = isSports;
                currentChannel.IsSeries = isSeries;

                if (isKids)
                {
                    currentChannel.AddTag("Kids");
                }

                await currentChannel.UpdateToRepositoryAsync(ItemUpdateType.MetadataImport, cancellationToken).ConfigureAwait(false);
                await currentChannel.RefreshMetadata(
                    new MetadataRefreshOptions(new DirectoryService(_fileSystem))
                    {
                        ForceSave = true
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting programs for channel {Name}", currentChannel.Name);
            }

            numComplete++;
            double percent = numComplete / (double)allChannelsList.Count;

            progress.Report((85 * percent) + 15);
        }

        progress.Report(100);
        return new Tuple<List<Guid>, List<Guid>>(channels, programIds);
    }

    private void CleanDatabase(Guid[] currentIdList, BaseItemKind[] validTypes, IProgress<double> progress, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> list = _itemRepo.GetItemIdsList(new InternalItemsQuery
        {
            IncludeItemTypes = validTypes,
            DtoOptions = new DtoOptions(false)
        });

        int numComplete = 0;

        foreach (Guid itemId in list)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (itemId.IsEmpty())
            {
                // Somehow some invalid data got into the db. It probably predates the boundary checking
                continue;
            }

            if (!currentIdList.Contains(itemId))
            {
                BaseItem? item = _libraryManager.GetItemById(itemId);

                if (item is not null)
                {
                    _libraryManager.DeleteItem(
                        item,
                        new DeleteOptions
                        {
                            DeleteFileLocation = false,
                            DeleteFromExternalProvider = false
                        },
                        false);
                }
            }

            numComplete++;
            double percent = numComplete / (double)list.Count;

            progress.Report(100 * percent);
        }
    }

    private async Task<LiveTvChannel> GetChannel(
        ChannelInfo channelInfo,
        string serviceName,
        BaseItem parentFolder,
        CancellationToken cancellationToken)
    {
        Guid parentFolderId = parentFolder.Id;
        bool isNew = false;
        bool forceUpdate = false;

        Guid id = _tvDtoService.GetInternalChannelId(serviceName, channelInfo.Id);

        if (_libraryManager.GetItemById(id) is not LiveTvChannel item)
        {
            item = new LiveTvChannel
            {
                Name = channelInfo.Name,
                Id = id,
                DateCreated = DateTime.UtcNow
            };

            isNew = true;
        }

        if (channelInfo.Tags is not null)
        {
            if (!channelInfo.Tags.SequenceEqual(item.Tags, StringComparer.OrdinalIgnoreCase))
            {
                isNew = true;
            }

            item.Tags = channelInfo.Tags;
        }

        if (!item.ParentId.Equals(parentFolderId))
        {
            isNew = true;
        }

        item.ParentId = parentFolderId;

        item.ChannelType = channelInfo.ChannelType;
        item.ServiceName = serviceName;

        if (!string.Equals(item.GetProviderId(ExternalServiceTag), serviceName, StringComparison.OrdinalIgnoreCase))
        {
            forceUpdate = true;
        }

        item.SetProviderId(ExternalServiceTag, serviceName);

        if (!string.Equals(channelInfo.Id, item.ExternalId, StringComparison.Ordinal))
        {
            forceUpdate = true;
        }

        item.ExternalId = channelInfo.Id;

        if (!string.Equals(channelInfo.Number, item.Number, StringComparison.Ordinal))
        {
            forceUpdate = true;
        }

        item.Number = channelInfo.Number;

        if (!string.Equals(channelInfo.Name, item.Name, StringComparison.Ordinal))
        {
            forceUpdate = true;
        }

        item.Name = channelInfo.Name;

        if (!item.HasImage(ImageType.Primary))
        {
            if (!string.IsNullOrWhiteSpace(channelInfo.ImagePath))
            {
                item.SetImagePath(ImageType.Primary, channelInfo.ImagePath);
                forceUpdate = true;
            }
            else if (!string.IsNullOrWhiteSpace(channelInfo.ImageUrl))
            {
                item.SetImagePath(ImageType.Primary, channelInfo.ImageUrl);
                forceUpdate = true;
            }
        }

        if (isNew)
        {
            _libraryManager.CreateItem(item, parentFolder);
        }
        else if (forceUpdate)
        {
            await _libraryManager.UpdateItemAsync(item, parentFolder, ItemUpdateType.MetadataImport, cancellationToken).ConfigureAwait(false);
        }

        return item;
    }

    private (LiveTvProgram Item, bool IsNew, bool IsUpdated) GetProgram(
        ProgramInfo info,
        Dictionary<Guid, LiveTvProgram> allExistingPrograms,
        LiveTvChannel channel)
    {
        Guid id = _tvDtoService.GetInternalProgramId(info.Id);

        bool isNew = false;
        bool forceUpdate = false;

        if (!allExistingPrograms.TryGetValue(id, out LiveTvProgram? item))
        {
            isNew = true;
            item = new LiveTvProgram
            {
                Name = info.Name,
                Id = id,
                DateCreated = DateTime.UtcNow,
                DateModified = DateTime.UtcNow
            };

            item.TrySetProviderId(EtagKey, info.Etag);
        }

        if (!string.Equals(info.ShowId, item.ShowId, StringComparison.OrdinalIgnoreCase))
        {
            item.ShowId = info.ShowId;
            forceUpdate = true;
        }

        Guid channelId = channel.Id;
        if (!item.ParentId.Equals(channelId))
        {
            item.ParentId = channel.Id;
            forceUpdate = true;
        }

        item.Audio = info.Audio;
        item.ChannelId = channelId;
        item.CommunityRating = info.CommunityRating;
        item.EpisodeTitle = info.EpisodeTitle;
        item.ExternalId = info.Id;

        string seriesId = info.SeriesId;
        if (!string.IsNullOrWhiteSpace(seriesId) && !string.Equals(item.ExternalSeriesId, seriesId, StringComparison.OrdinalIgnoreCase))
        {
            item.ExternalSeriesId = seriesId;
            forceUpdate = true;
        }

        bool isSeries = info.IsSeries || !string.IsNullOrEmpty(info.EpisodeTitle);
        if (isSeries || !string.IsNullOrEmpty(info.EpisodeTitle))
        {
            item.SeriesName = info.Name;
        }

        List<string> tags = new List<string>();
        if (info.IsLive)
        {
            tags.Add("Live");
        }

        if (info.IsPremiere)
        {
            tags.Add("Premiere");
        }

        if (info.IsNews)
        {
            tags.Add("News");
        }

        if (info.IsSports)
        {
            tags.Add("Sports");
        }

        if (info.IsKids)
        {
            tags.Add("Kids");
        }

        if (info.IsRepeat)
        {
            tags.Add("Repeat");
        }

        if (info.IsMovie)
        {
            tags.Add("Movie");
        }

        if (isSeries)
        {
            tags.Add("Series");
        }

        item.Tags = tags.ToArray();
        item.Genres = info.Genres.ToArray();

        if (info.IsHD ?? false)
        {
            item.Width = 1280;
            item.Height = 720;
        }

        item.IsMovie = info.IsMovie;
        item.IsRepeat = info.IsRepeat;
        if (item.IsSeries != isSeries)
        {
            item.IsSeries = isSeries;
            forceUpdate = true;
        }

        item.Name = info.Name;
        item.OfficialRating = info.OfficialRating;
        item.Overview = info.Overview;
        item.RunTimeTicks = (info.EndDate - info.StartDate).Ticks;
        foreach (KeyValuePair<string, string> providerId in info.SeriesProviderIds)
        {
            info.ProviderIds["Series" + providerId.Key] = providerId.Value;
        }

        item.ProviderIds = info.ProviderIds;
        if (item.StartDate != info.StartDate)
        {
            item.StartDate = info.StartDate;
            forceUpdate = true;
        }

        if (item.EndDate != info.EndDate)
        {
            item.EndDate = info.EndDate;
            forceUpdate = true;
        }

        item.ProductionYear = info.ProductionYear;
        if (!isSeries || info.IsRepeat)
        {
            item.PremiereDate = info.OriginalAirDate;
        }

        item.IndexNumber = info.EpisodeNumber;
        item.ParentIndexNumber = info.SeasonNumber;

        forceUpdate |= UpdateImages(item, info);

        if (isNew)
        {
            item.OnMetadataChanged();

            return (item, true, false);
        }

        bool isUpdated = forceUpdate;
        string etag = info.Etag;
        if (string.IsNullOrWhiteSpace(etag))
        {
            isUpdated = true;
        }
        else if (!string.Equals(etag, item.GetProviderId(EtagKey), StringComparison.OrdinalIgnoreCase))
        {
            item.SetProviderId(EtagKey, etag);
            isUpdated = true;
        }

        if (isUpdated)
        {
            item.OnMetadataChanged();

            return (item, false, true);
        }

        return (item, false, false);
    }

    private static bool UpdateImages(BaseItem item, ProgramInfo info)
    {
        bool updated = false;

        // Primary
        updated |= UpdateImage(ImageType.Primary, item, info);

        // Thumbnail
        updated |= UpdateImage(ImageType.Thumb, item, info);

        // Logo
        updated |= UpdateImage(ImageType.Logo, item, info);

        // Backdrop
        updated |= UpdateImage(ImageType.Backdrop, item, info);

        return updated;
    }

    private static bool UpdateImage(ImageType imageType, BaseItem item, ProgramInfo info)
    {
        ItemImageInfo? image = item.GetImages(imageType).FirstOrDefault();
        string? currentImagePath = image?.Path;
        string? newImagePath = imageType switch
        {
            ImageType.Primary => info.ImagePath,
            _ => null
        };
        string? newImageUrl = imageType switch
        {
            ImageType.Backdrop => info.BackdropImageUrl,
            ImageType.Logo => info.LogoImageUrl,
            ImageType.Primary => info.ImageUrl,
            ImageType.Thumb => info.ThumbImageUrl,
            _ => null
        };

        bool sameImage = (currentImagePath?.Equals(newImageUrl, StringComparison.OrdinalIgnoreCase) ?? false)
                                || (currentImagePath?.Equals(newImagePath, StringComparison.OrdinalIgnoreCase) ?? false);
        if (sameImage)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(newImagePath))
        {
            item.SetImage(
                new ItemImageInfo
                {
                    Path = newImagePath,
                    Type = imageType
                },
                0);

            return true;
        }

        if (!string.IsNullOrWhiteSpace(newImageUrl))
        {
            item.SetImage(
                new ItemImageInfo
                {
                    Path = newImageUrl,
                    Type = imageType
                },
                0);

            return true;
        }

        item.RemoveImage(image);

        return false;
    }

    private async Task PreCacheImages(IReadOnlyList<BaseItem> programs, DateTime maxCacheDate)
    {
        await Parallel.ForEachAsync(
            programs
                .Where(p => p.EndDate.HasValue && p.EndDate.Value < maxCacheDate)
                .DistinctBy(p => p.Id),
            _cacheParallelOptions,
            async (program, cancellationToken) =>
            {
                for (int i = 0; i < program.ImageInfos.Length; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    ItemImageInfo imageInfo = program.ImageInfos[i];
                    if (!imageInfo.IsLocalFile)
                    {
                        _logger.LogDebug("Caching image locally: {Url}", imageInfo.Path);
                        try
                        {
                            program.ImageInfos[i] = await _libraryManager.ConvertImageToLocal(
                                    program,
                                    imageInfo,
                                    imageIndex: 0,
                                    removeOnFailure: false)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Unable to pre-cache {Url}", imageInfo.Path);
                        }
                    }
                }
            }).ConfigureAwait(false);
    }
}
