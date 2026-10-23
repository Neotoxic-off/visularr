#nullable disable

#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Data.Events;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.LiveTv.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Sorting;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace Jellyfin.LiveTv
{
    /// <summary>
    /// Class LiveTvManager.
    /// </summary>
    public class LiveTvManager : ILiveTvManager
    {
        private readonly IServerConfigurationManager _config;
        private readonly ILogger<LiveTvManager> _logger;
        private readonly IUserManager _userManager;
        private readonly IDtoService _dtoService;
        private readonly IUserDataManager _userDataManager;
        private readonly ILibraryManager _libraryManager;
        private readonly ILocalizationManager _localization;
        private readonly IChannelManager _channelManager;
        private readonly IRecordingsManager _recordingsManager;
        private readonly LiveTvDtoService _tvDtoService;
        private readonly ILiveTvService[] _services;

        public LiveTvManager(
            IServerConfigurationManager config,
            ILogger<LiveTvManager> logger,
            IUserDataManager userDataManager,
            IDtoService dtoService,
            IUserManager userManager,
            ILibraryManager libraryManager,
            ILocalizationManager localization,
            IChannelManager channelManager,
            IRecordingsManager recordingsManager,
            LiveTvDtoService liveTvDtoService,
            IEnumerable<ILiveTvService> services)
        {
            _config = config;
            _logger = logger;
            _userManager = userManager;
            _libraryManager = libraryManager;
            _localization = localization;
            _dtoService = dtoService;
            _userDataManager = userDataManager;
            _channelManager = channelManager;
            _tvDtoService = liveTvDtoService;
            _recordingsManager = recordingsManager;
            _services = services.ToArray();

            DefaultLiveTvService defaultService = _services.OfType<DefaultLiveTvService>().First();
            defaultService.TimerCreated += OnEmbyTvTimerCreated;
            defaultService.TimerCancelled += OnEmbyTvTimerCancelled;
        }

        public event EventHandler<GenericEventArgs<TimerEventInfo>> SeriesTimerCancelled;

        public event EventHandler<GenericEventArgs<TimerEventInfo>> TimerCancelled;

        public event EventHandler<GenericEventArgs<TimerEventInfo>> TimerCreated;

        public event EventHandler<GenericEventArgs<TimerEventInfo>> SeriesTimerCreated;

        /// <summary>
        /// Gets the services.
        /// </summary>
        /// <value>The services.</value>
        public IReadOnlyList<ILiveTvService> Services => _services;

        private void OnEmbyTvTimerCancelled(object sender, GenericEventArgs<string> e)
        {
            string timerId = e.Argument;

            TimerCancelled?.Invoke(this, new GenericEventArgs<TimerEventInfo>(new TimerEventInfo(timerId)));
        }

        private void OnEmbyTvTimerCreated(object sender, GenericEventArgs<TimerInfo> e)
        {
            TimerInfo timer = e.Argument;

            TimerCreated?.Invoke(this, new GenericEventArgs<TimerEventInfo>(
                new TimerEventInfo(timer.Id)
                {
                    ProgramId = _tvDtoService.GetInternalProgramId(timer.ProgramId)
                }));
        }

        public QueryResult<BaseItem> GetInternalChannels(LiveTvChannelQuery query, DtoOptions dtoOptions, CancellationToken cancellationToken)
        {
            User user = query.UserId.Equals(default)
                ? null
                : _userManager.GetUserById(query.UserId);

            Folder topFolder = GetInternalLiveTvFolder(cancellationToken);

            InternalItemsQuery internalQuery = new InternalItemsQuery(user)
            {
                IsMovie = query.IsMovie,
                IsNews = query.IsNews,
                IsKids = query.IsKids,
                IsSports = query.IsSports,
                IsSeries = query.IsSeries,
                IncludeItemTypes = [BaseItemKind.LiveTvChannel],
                TopParentIds = [topFolder.Id],
                IsFavorite = query.IsFavorite,
                IsLiked = query.IsLiked,
                StartIndex = query.StartIndex,
                Limit = query.Limit,
                DtoOptions = dtoOptions
            };

            List<(ItemSortBy OrderBy, SortOrder SortOrder)> orderBy = internalQuery.OrderBy.ToList();

            orderBy.AddRange(query.SortBy.Select(i => (i, query.SortOrder ?? SortOrder.Ascending)));

            if (query.EnableFavoriteSorting)
            {
                orderBy.Insert(0, (ItemSortBy.IsFavoriteOrLiked, SortOrder.Descending));
            }

            if (internalQuery.OrderBy.All(i => i.OrderBy != ItemSortBy.SortName))
            {
                orderBy.Add((ItemSortBy.SortName, SortOrder.Ascending));
            }

            internalQuery.OrderBy = orderBy.ToArray();

            return _libraryManager.GetItemsResult(internalQuery);
        }

        private ILiveTvService GetService(LiveTvChannel item)
        {
            string name = item.ServiceName;
            return GetService(name);
        }

        private ILiveTvService GetService(LiveTvProgram item)
        {
            LiveTvChannel channel = _libraryManager.GetItemById(item.ChannelId) as LiveTvChannel;

            return GetService(channel);
        }

        private ILiveTvService GetService(string name)
            => Array.Find(_services, x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "No service with the name '{0}' can be found.",
                        name));

        public async Task<BaseItemDto> GetProgram(string id, CancellationToken cancellationToken, User user = null)
        {
            BaseItem program = _libraryManager.GetItemById(id);

            BaseItemDto dto = _dtoService.GetBaseItemDto(program, new DtoOptions(), user);

            List<(BaseItemDto ItemDto, string ExternalId, string ExternalSeriesId)> list = new List<(BaseItemDto ItemDto, string ExternalId, string ExternalSeriesId)>
            {
                (dto, program.ExternalId, program.ExternalSeriesId)
            };

            await AddRecordingInfo(list, cancellationToken).ConfigureAwait(false);

            return dto;
        }

        public async Task<QueryResult<BaseItemDto>> GetPrograms(InternalItemsQuery query, DtoOptions options, CancellationToken cancellationToken)
        {
            User user = query.User;

            Folder topFolder = GetInternalLiveTvFolder(cancellationToken);

            if (query.OrderBy.Count == 0)
            {
                // Unless something else was specified, order by start date to take advantage of a specialized index
                query.OrderBy =
                [
                    (ItemSortBy.StartDate, SortOrder.Ascending)
                ];
            }

            RemoveFields(options);

            InternalItemsQuery internalQuery = new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.LiveTvProgram],
                MinEndDate = query.MinEndDate,
                MinStartDate = query.MinStartDate,
                MaxEndDate = query.MaxEndDate,
                MaxStartDate = query.MaxStartDate,
                ChannelIds = query.ChannelIds,
                IsMovie = query.IsMovie,
                IsSeries = query.IsSeries,
                IsSports = query.IsSports,
                IsKids = query.IsKids,
                IsNews = query.IsNews,
                Genres = query.Genres,
                GenreIds = query.GenreIds,
                StartIndex = query.StartIndex,
                Limit = query.Limit,
                OrderBy = query.OrderBy,
                EnableTotalRecordCount = query.EnableTotalRecordCount,
                TopParentIds = [topFolder.Id],
                Name = query.Name,
                DtoOptions = options,
                HasAired = query.HasAired,
                IsAiring = query.IsAiring
            };

            if (!string.IsNullOrWhiteSpace(query.SeriesTimerId))
            {
                QueryResult<SeriesTimerInfo> seriesTimers = await GetSeriesTimersInternal(new SeriesTimerQuery(), cancellationToken).ConfigureAwait(false);
                SeriesTimerInfo seriesTimer = seriesTimers.Items.FirstOrDefault(i => string.Equals(_tvDtoService.GetInternalSeriesTimerId(i.Id).ToString("N", CultureInfo.InvariantCulture), query.SeriesTimerId, StringComparison.OrdinalIgnoreCase));
                if (seriesTimer is not null)
                {
                    internalQuery.ExternalSeriesId = seriesTimer.SeriesId;

                    if (string.IsNullOrWhiteSpace(seriesTimer.SeriesId))
                    {
                        // Better to return nothing than every program in the database
                        return new QueryResult<BaseItemDto>();
                    }
                }
                else
                {
                    // Better to return nothing than every program in the database
                    return new QueryResult<BaseItemDto>();
                }
            }

            QueryResult<BaseItem> queryResult = _libraryManager.QueryItems(internalQuery);

            IReadOnlyList<BaseItemDto> returnArray = _dtoService.GetBaseItemDtos(queryResult.Items, options, user);

            return new QueryResult<BaseItemDto>(
                query.StartIndex,
                queryResult.TotalRecordCount,
                returnArray);
        }

        public QueryResult<BaseItem> GetRecommendedProgramsInternal(InternalItemsQuery query, DtoOptions options, CancellationToken cancellationToken)
        {
            User user = query.User;

            Folder topFolder = GetInternalLiveTvFolder(cancellationToken);

            InternalItemsQuery internalQuery = new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.LiveTvProgram],
                IsAiring = query.IsAiring,
                HasAired = query.HasAired,
                IsNews = query.IsNews,
                IsMovie = query.IsMovie,
                IsSeries = query.IsSeries,
                IsSports = query.IsSports,
                IsKids = query.IsKids,
                EnableTotalRecordCount = query.EnableTotalRecordCount,
                OrderBy = [(ItemSortBy.StartDate, SortOrder.Ascending)],
                TopParentIds = [topFolder.Id],
                DtoOptions = options,
                GenreIds = query.GenreIds
            };

            if (query.Limit.HasValue)
            {
                internalQuery.Limit = Math.Max(query.Limit.Value * 4, 200);
            }

            IReadOnlyList<BaseItem> programList = _libraryManager.QueryItems(internalQuery).Items;
            int totalCount = programList.Count;

            IOrderedEnumerable<LiveTvProgram> orderedPrograms = programList.Cast<LiveTvProgram>().OrderBy(i => i.StartDate.Date);

            if (query.IsAiring ?? false)
            {
                orderedPrograms = orderedPrograms
                    .ThenByDescending(i => GetRecommendationScore(i, user, true));
            }

            IEnumerable<BaseItem> programs = orderedPrograms;

            if (query.Limit.HasValue)
            {
                programs = programs.Take(query.Limit.Value);
            }

            return new QueryResult<BaseItem>(
                query.StartIndex,
                totalCount,
                programs.ToArray());
        }

        public Task<QueryResult<BaseItemDto>> GetRecommendedProgramsAsync(InternalItemsQuery query, DtoOptions options, CancellationToken cancellationToken)
        {
            if (!(query.IsAiring ?? false))
            {
                return GetPrograms(query, options, cancellationToken);
            }

            RemoveFields(options);

            QueryResult<BaseItem> internalResult = GetRecommendedProgramsInternal(query, options, cancellationToken);

            return Task.FromResult(new QueryResult<BaseItemDto>(
                query.StartIndex,
                internalResult.TotalRecordCount,
                _dtoService.GetBaseItemDtos(internalResult.Items, options, query.User)));
        }

        private int GetRecommendationScore(LiveTvProgram program, User user, bool factorChannelWatchCount)
        {
            int score = 0;

            if (program.IsLive)
            {
                score++;
            }

            if (program.IsSeries && !program.IsRepeat)
            {
                score++;
            }

            BaseItem channel = _libraryManager.GetItemById(program.ChannelId);

            if (channel is null)
            {
                return score;
            }

            UserItemData channelUserdata = _userDataManager.GetUserData(user, channel);

            if (channelUserdata.Likes.HasValue)
            {
                score += channelUserdata.Likes.Value ? 2 : -2;
            }

            if (channelUserdata.IsFavorite)
            {
                score += 3;
            }

            if (factorChannelWatchCount)
            {
                score += channelUserdata.PlayCount;
            }

            return score;
        }

        private async Task AddRecordingInfo(IEnumerable<(BaseItemDto ItemDto, string ExternalId, string ExternalSeriesId)> programs, CancellationToken cancellationToken)
        {
            IReadOnlyList<TimerInfo> timerList = null;
            IReadOnlyList<SeriesTimerInfo> seriesTimerList = null;

            foreach ((BaseItemDto ItemDto, string ExternalId, string ExternalSeriesId) programTuple in programs)
            {
                BaseItemDto program = programTuple.ItemDto;
                string externalProgramId = programTuple.ExternalId;
                string externalSeriesId = programTuple.ExternalSeriesId;

                timerList ??= (await GetTimersInternal(new TimerQuery(), cancellationToken).ConfigureAwait(false)).Items;

                TimerInfo timer = timerList.FirstOrDefault(i => string.Equals(i.ProgramId, externalProgramId, StringComparison.OrdinalIgnoreCase));
                bool foundSeriesTimer = false;

                if (timer is not null)
                {
                    if (timer.Status != RecordingStatus.Cancelled && timer.Status != RecordingStatus.Error)
                    {
                        program.TimerId = _tvDtoService.GetInternalTimerId(timer.Id);

                        program.Status = timer.Status.ToString();
                    }

                    if (!string.IsNullOrEmpty(timer.SeriesTimerId))
                    {
                        program.SeriesTimerId = _tvDtoService.GetInternalSeriesTimerId(timer.SeriesTimerId)
                            .ToString("N", CultureInfo.InvariantCulture);

                        foundSeriesTimer = true;
                    }
                }

                if (foundSeriesTimer || string.IsNullOrWhiteSpace(externalSeriesId))
                {
                    continue;
                }

                seriesTimerList ??= (await GetSeriesTimersInternal(new SeriesTimerQuery(), cancellationToken).ConfigureAwait(false)).Items;

                SeriesTimerInfo seriesTimer = seriesTimerList.FirstOrDefault(i => string.Equals(i.SeriesId, externalSeriesId, StringComparison.OrdinalIgnoreCase));

                if (seriesTimer is not null)
                {
                    program.SeriesTimerId = _tvDtoService.GetInternalSeriesTimerId(seriesTimer.Id)
                        .ToString("N", CultureInfo.InvariantCulture);
                }
            }
        }

        private async Task<QueryResult<BaseItem>> GetEmbyRecordingsAsync(RecordingQuery query, DtoOptions dtoOptions, User user)
        {
            if (user is null)
            {
                return new QueryResult<BaseItem>();
            }

            BaseItem[] folders = await GetRecordingFoldersAsync(user, true).ConfigureAwait(false);
            Guid[] folderIds = Array.ConvertAll(folders, x => x.Id);

            List<BaseItemKind> excludeItemTypes = new List<BaseItemKind>();

            if (folderIds.Length == 0)
            {
                return new QueryResult<BaseItem>();
            }

            List<BaseItemKind> includeItemTypes = new List<BaseItemKind>();
            List<string> genres = new List<string>();

            if (query.IsMovie.HasValue)
            {
                if (query.IsMovie.Value)
                {
                    includeItemTypes.Add(BaseItemKind.Movie);
                }
                else
                {
                    excludeItemTypes.Add(BaseItemKind.Movie);
                }
            }

            if (query.IsSeries.HasValue)
            {
                if (query.IsSeries.Value)
                {
                    includeItemTypes.Add(BaseItemKind.Episode);
                }
                else
                {
                    excludeItemTypes.Add(BaseItemKind.Episode);
                }
            }

            if (query.IsSports ?? false)
            {
                genres.Add("Sports");
            }

            if (query.IsKids ?? false)
            {
                genres.Add("Kids");
                genres.Add("Children");
                genres.Add("Family");
            }

            int? limit = query.Limit;

            if (query.IsInProgress ?? false)
            {
                // limit = (query.Limit ?? 10) * 2;
                limit = null;

                // var allActivePaths = EmbyTV.EmbyTV.Current.GetAllActiveRecordings().Select(i => i.Path).ToArray();
                // var items = allActivePaths.Select(i => _libraryManager.FindByPath(i, false)).Where(i => i is not null).ToArray();

                // return new QueryResult<BaseItem>
                // {
                //    Items = items,
                //    TotalRecordCount = items.Length
                // };

                dtoOptions.Fields = dtoOptions.Fields.Concat([ItemFields.Tags]).Distinct().ToArray();
            }

            QueryResult<BaseItem> result = _libraryManager.GetItemsResult(new InternalItemsQuery(user)
            {
                MediaTypes = [MediaType.Video],
                Recursive = true,
                AncestorIds = folderIds,
                IsFolder = false,
                IsVirtualItem = false,
                Limit = limit,
                StartIndex = query.StartIndex,
                OrderBy = [(ItemSortBy.DateCreated, SortOrder.Descending)],
                EnableTotalRecordCount = query.EnableTotalRecordCount,
                IncludeItemTypes = includeItemTypes.ToArray(),
                ExcludeItemTypes = excludeItemTypes.ToArray(),
                Genres = genres.ToArray(),
                DtoOptions = dtoOptions
            });

            if (query.IsInProgress ?? false)
            {
                // TODO: Fix The co-variant conversion between Video[] and BaseItem[], this can generate runtime issues.
                result.Items = result
                    .Items
                    .OfType<Video>()
                    .Where(i => !i.IsCompleteMedia)
                    .ToArray();

                result.TotalRecordCount = result.Items.Count;
            }

            return result;
        }

        public Task AddInfoToProgramDto(IReadOnlyCollection<(BaseItem Item, BaseItemDto ItemDto)> programs, IReadOnlyList<ItemFields> fields, User user = null)
        {
            List<(BaseItemDto Dto, string ExternalId, string ExternalSeriesId)> programTuples = new List<(BaseItemDto Dto, string ExternalId, string ExternalSeriesId)>();
            bool hasChannelImage = fields.Contains(ItemFields.ChannelImage);
            bool hasChannelInfo = fields.Contains(ItemFields.ChannelInfo);

            foreach ((BaseItem item, BaseItemDto dto) in programs)
            {
                LiveTvProgram program = (LiveTvProgram)item;

                dto.StartDate = program.StartDate;
                dto.EpisodeTitle = program.EpisodeTitle;
                dto.IsRepeat |= program.IsRepeat;
                dto.IsMovie |= program.IsMovie;
                dto.IsSeries |= program.IsSeries;
                dto.IsSports |= program.IsSports;
                dto.IsLive |= program.IsLive;
                dto.IsNews |= program.IsNews;
                dto.IsKids |= program.IsKids;
                dto.IsPremiere |= program.IsPremiere;

                if (hasChannelInfo || hasChannelImage)
                {
                    BaseItem channel = _libraryManager.GetItemById(program.ChannelId);

                    if (channel is LiveTvChannel liveChannel)
                    {
                        dto.ChannelName = liveChannel.Name;
                        dto.MediaType = liveChannel.MediaType;
                        dto.ChannelNumber = liveChannel.Number;

                        if (hasChannelImage && liveChannel.HasImage(ImageType.Primary))
                        {
                            dto.ChannelPrimaryImageTag = _tvDtoService.GetImageTag(liveChannel);
                        }
                    }
                }

                programTuples.Add((dto, program.ExternalId, program.ExternalSeriesId));
            }

            return AddRecordingInfo(programTuples, CancellationToken.None);
        }

        public void AddInfoToRecordingDto(BaseItem item, BaseItemDto dto, ActiveRecordingInfo activeRecordingInfo, User user = null)
        {
            TimerInfo info = activeRecordingInfo.Timer;

            BaseItem channel = string.IsNullOrWhiteSpace(info.ChannelId)
                ? null
                : _libraryManager.GetItemById(_tvDtoService.GetInternalChannelId(DefaultLiveTvService.ServiceName, info.ChannelId));

            dto.SeriesTimerId = string.IsNullOrEmpty(info.SeriesTimerId)
                ? null
                : _tvDtoService.GetInternalSeriesTimerId(info.SeriesTimerId).ToString("N", CultureInfo.InvariantCulture);

            dto.TimerId = string.IsNullOrEmpty(info.Id)
                ? null
                : _tvDtoService.GetInternalTimerId(info.Id);

            DateTime startDate = info.StartDate;
            DateTime endDate = info.EndDate;

            dto.StartDate = startDate;
            dto.EndDate = endDate;
            dto.Status = info.Status.ToString();
            dto.IsRepeat = info.IsRepeat;
            dto.EpisodeTitle = info.EpisodeTitle;
            dto.IsMovie = info.IsMovie;
            dto.IsSeries = info.IsSeries;
            dto.IsSports = info.IsSports;
            dto.IsLive = info.IsLive;
            dto.IsNews = info.IsNews;
            dto.IsKids = info.IsKids;
            dto.IsPremiere = info.IsPremiere;

            if (info.Status == RecordingStatus.InProgress)
            {
                startDate = info.StartDate.AddSeconds(0 - info.PrePaddingSeconds);
                endDate = info.EndDate.AddSeconds(info.PostPaddingSeconds);

                long now = DateTime.UtcNow.Ticks;
                long start = startDate.Ticks;
                long end = endDate.Ticks;

                long pct = now - start;

                pct /= end;
                pct *= 100;
                dto.CompletionPercentage = pct;
            }

            if (channel is not null)
            {
                dto.ChannelName = channel.Name;

                if (channel.HasImage(ImageType.Primary))
                {
                    dto.ChannelPrimaryImageTag = _tvDtoService.GetImageTag(channel);
                }
            }
        }

        public async Task<QueryResult<BaseItemDto>> GetRecordingsAsync(RecordingQuery query, DtoOptions options)
        {
            User user = query.UserId.Equals(default)
                ? null
                : _userManager.GetUserById(query.UserId);

            RemoveFields(options);

            QueryResult<BaseItem> internalResult = await GetEmbyRecordingsAsync(query, options, user).ConfigureAwait(false);

            IReadOnlyList<BaseItemDto> returnArray = _dtoService.GetBaseItemDtos(internalResult.Items, options, user);

            return new QueryResult<BaseItemDto>(
                query.StartIndex,
                internalResult.TotalRecordCount,
                returnArray);
        }

        private async Task<QueryResult<TimerInfo>> GetTimersInternal(TimerQuery query, CancellationToken cancellationToken)
        {
            IEnumerable<Task<IEnumerable<Tuple<TimerInfo, ILiveTvService>>>> tasks = _services.Select(async i =>
            {
                try
                {
                    IEnumerable<TimerInfo> recs = await i.GetTimersAsync(cancellationToken).ConfigureAwait(false);
                    return recs.Select(r => new Tuple<TimerInfo, ILiveTvService>(r, i));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting recordings");
                    return new List<Tuple<TimerInfo, ILiveTvService>>();
                }
            });
            IEnumerable<Tuple<TimerInfo, ILiveTvService>>[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
            IEnumerable<Tuple<TimerInfo, ILiveTvService>> timers = results.SelectMany(i => i.ToList());

            if (query.IsActive.HasValue)
            {
                if (query.IsActive.Value)
                {
                    timers = timers.Where(i => i.Item1.Status == RecordingStatus.InProgress);
                }
                else
                {
                    timers = timers.Where(i => i.Item1.Status != RecordingStatus.InProgress);
                }
            }

            if (query.IsScheduled.HasValue)
            {
                if (query.IsScheduled.Value)
                {
                    timers = timers.Where(i => i.Item1.Status == RecordingStatus.New);
                }
                else
                {
                    timers = timers.Where(i => i.Item1.Status != RecordingStatus.New);
                }
            }

            if (!string.IsNullOrEmpty(query.ChannelId))
            {
                Guid guid = new Guid(query.ChannelId);
                timers = timers.Where(i => _tvDtoService.GetInternalChannelId(i.Item2.Name, i.Item1.ChannelId).Equals(guid));
            }

            if (!string.IsNullOrEmpty(query.SeriesTimerId))
            {
                Guid guid = new Guid(query.SeriesTimerId);

                timers = timers
                    .Where(i => _tvDtoService.GetInternalSeriesTimerId(i.Item1.SeriesTimerId).Equals(guid));
            }

            if (!string.IsNullOrEmpty(query.Id))
            {
                timers = timers
                    .Where(i => string.Equals(_tvDtoService.GetInternalTimerId(i.Item1.Id), query.Id, StringComparison.OrdinalIgnoreCase));
            }

            TimerInfo[] returnArray = timers
                .Select(i => i.Item1)
                .OrderBy(i => i.StartDate)
                .ToArray();

            return new QueryResult<TimerInfo>(returnArray);
        }

        public async Task<QueryResult<TimerInfoDto>> GetTimers(TimerQuery query, CancellationToken cancellationToken)
        {
            IEnumerable<Task<IEnumerable<Tuple<TimerInfo, ILiveTvService>>>> tasks = _services.Select(async i =>
            {
                try
                {
                    IEnumerable<TimerInfo> recs = await i.GetTimersAsync(cancellationToken).ConfigureAwait(false);
                    return recs.Select(r => new Tuple<TimerInfo, ILiveTvService>(r, i));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting recordings");
                    return new List<Tuple<TimerInfo, ILiveTvService>>();
                }
            });
            IEnumerable<Tuple<TimerInfo, ILiveTvService>>[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
            IEnumerable<Tuple<TimerInfo, ILiveTvService>> timers = results.SelectMany(i => i.ToList());

            if (query.IsActive.HasValue)
            {
                if (query.IsActive.Value)
                {
                    timers = timers.Where(i => i.Item1.Status == RecordingStatus.InProgress);
                }
                else
                {
                    timers = timers.Where(i => i.Item1.Status != RecordingStatus.InProgress);
                }
            }

            if (query.IsScheduled.HasValue)
            {
                if (query.IsScheduled.Value)
                {
                    timers = timers.Where(i => i.Item1.Status == RecordingStatus.New);
                }
                else
                {
                    timers = timers.Where(i => i.Item1.Status != RecordingStatus.New);
                }
            }

            if (!string.IsNullOrEmpty(query.ChannelId))
            {
                Guid guid = new Guid(query.ChannelId);
                timers = timers.Where(i => _tvDtoService.GetInternalChannelId(i.Item2.Name, i.Item1.ChannelId).Equals(guid));
            }

            if (!string.IsNullOrEmpty(query.SeriesTimerId))
            {
                Guid guid = new Guid(query.SeriesTimerId);

                timers = timers
                    .Where(i => _tvDtoService.GetInternalSeriesTimerId(i.Item1.SeriesTimerId).Equals(guid));
            }

            if (!string.IsNullOrEmpty(query.Id))
            {
                timers = timers
                    .Where(i => string.Equals(_tvDtoService.GetInternalTimerId(i.Item1.Id), query.Id, StringComparison.OrdinalIgnoreCase));
            }

            List<TimerInfoDto> returnList = new List<TimerInfoDto>();

            foreach (Tuple<TimerInfo, ILiveTvService> i in timers)
            {
                LiveTvProgram program = string.IsNullOrEmpty(i.Item1.ProgramId) ?
                    null :
                    _libraryManager.GetItemById(_tvDtoService.GetInternalProgramId(i.Item1.ProgramId)) as LiveTvProgram;

                BaseItem channel = string.IsNullOrEmpty(i.Item1.ChannelId) ? null : _libraryManager.GetItemById(_tvDtoService.GetInternalChannelId(i.Item2.Name, i.Item1.ChannelId));

                returnList.Add(_tvDtoService.GetTimerInfoDto(i.Item1, i.Item2, program, channel));
            }

            TimerInfoDto[] returnArray = returnList
                .OrderBy(i => i.StartDate)
                .ToArray();

            return new QueryResult<TimerInfoDto>(returnArray);
        }

        public async Task CancelTimer(string id)
        {
            TimerInfoDto timer = await GetTimer(id, CancellationToken.None).ConfigureAwait(false);

            if (timer is null)
            {
                throw new ResourceNotFoundException(string.Format(CultureInfo.InvariantCulture, "Timer with Id {0} not found", id));
            }

            ILiveTvService service = GetService(timer.ServiceName);

            await service.CancelTimerAsync(timer.ExternalId, CancellationToken.None).ConfigureAwait(false);

            if (service is not DefaultLiveTvService)
            {
                TimerCancelled?.Invoke(this, new GenericEventArgs<TimerEventInfo>(new TimerEventInfo(id)));
            }
        }

        public async Task CancelSeriesTimer(string id)
        {
            SeriesTimerInfoDto timer = await GetSeriesTimer(id, CancellationToken.None).ConfigureAwait(false);

            if (timer is null)
            {
                throw new ResourceNotFoundException(string.Format(CultureInfo.InvariantCulture, "SeriesTimer with Id {0} not found", id));
            }

            ILiveTvService service = GetService(timer.ServiceName);

            await service.CancelSeriesTimerAsync(timer.ExternalId, CancellationToken.None).ConfigureAwait(false);

            SeriesTimerCancelled?.Invoke(this, new GenericEventArgs<TimerEventInfo>(new TimerEventInfo(id)));
        }

        public async Task<TimerInfoDto> GetTimer(string id, CancellationToken cancellationToken)
        {
            QueryResult<TimerInfoDto> results = await GetTimers(
                new TimerQuery
                {
                    Id = id
                },
                cancellationToken).ConfigureAwait(false);

            return results.Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<SeriesTimerInfoDto> GetSeriesTimer(string id, CancellationToken cancellationToken)
        {
            QueryResult<SeriesTimerInfoDto> results = await GetSeriesTimers(new SeriesTimerQuery(), cancellationToken).ConfigureAwait(false);

            return results.Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private async Task<QueryResult<SeriesTimerInfo>> GetSeriesTimersInternal(SeriesTimerQuery query, CancellationToken cancellationToken)
        {
            IEnumerable<Task<IEnumerable<Tuple<SeriesTimerInfo, ILiveTvService>>>> tasks = _services.Select(async i =>
            {
                try
                {
                    IEnumerable<SeriesTimerInfo> recs = await i.GetSeriesTimersAsync(cancellationToken).ConfigureAwait(false);
                    return recs.Select(r =>
                    {
                        r.ServiceName = i.Name;
                        return new Tuple<SeriesTimerInfo, ILiveTvService>(r, i);
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting recordings");
                    return new List<Tuple<SeriesTimerInfo, ILiveTvService>>();
                }
            });
            IEnumerable<Tuple<SeriesTimerInfo, ILiveTvService>>[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
            IEnumerable<Tuple<SeriesTimerInfo, ILiveTvService>> timers = results.SelectMany(i => i.ToList());

            if (string.Equals(query.SortBy, "Priority", StringComparison.OrdinalIgnoreCase))
            {
                timers = query.SortOrder == SortOrder.Descending ?
                    timers.OrderBy(i => i.Item1.Priority).ThenByStringDescending(i => i.Item1.Name) :
                    timers.OrderByDescending(i => i.Item1.Priority).ThenByString(i => i.Item1.Name);
            }
            else
            {
                timers = query.SortOrder == SortOrder.Descending ?
                    timers.OrderByStringDescending(i => i.Item1.Name) :
                    timers.OrderByString(i => i.Item1.Name);
            }

            SeriesTimerInfo[] returnArray = timers
                .Select(i => i.Item1)
                .ToArray();

            return new QueryResult<SeriesTimerInfo>(returnArray);
        }

        public async Task<QueryResult<SeriesTimerInfoDto>> GetSeriesTimers(SeriesTimerQuery query, CancellationToken cancellationToken)
        {
            IEnumerable<Task<IEnumerable<Tuple<SeriesTimerInfo, ILiveTvService>>>> tasks = _services.Select(async i =>
            {
                try
                {
                    IEnumerable<SeriesTimerInfo> recs = await i.GetSeriesTimersAsync(cancellationToken).ConfigureAwait(false);
                    return recs.Select(r => new Tuple<SeriesTimerInfo, ILiveTvService>(r, i));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting recordings");
                    return new List<Tuple<SeriesTimerInfo, ILiveTvService>>();
                }
            });
            IEnumerable<Tuple<SeriesTimerInfo, ILiveTvService>>[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
            IEnumerable<Tuple<SeriesTimerInfo, ILiveTvService>> timers = results.SelectMany(i => i.ToList());

            if (string.Equals(query.SortBy, "Priority", StringComparison.OrdinalIgnoreCase))
            {
                timers = query.SortOrder == SortOrder.Descending ?
                    timers.OrderBy(i => i.Item1.Priority).ThenByStringDescending(i => i.Item1.Name) :
                    timers.OrderByDescending(i => i.Item1.Priority).ThenByString(i => i.Item1.Name);
            }
            else
            {
                timers = query.SortOrder == SortOrder.Descending ?
                    timers.OrderByStringDescending(i => i.Item1.Name) :
                    timers.OrderByString(i => i.Item1.Name);
            }

            SeriesTimerInfoDto[] returnArray = timers
                .Select(i =>
                {
                    string channelName = null;

                    if (!string.IsNullOrEmpty(i.Item1.ChannelId))
                    {
                        Guid internalChannelId = _tvDtoService.GetInternalChannelId(i.Item2.Name, i.Item1.ChannelId);
                        BaseItem channel = _libraryManager.GetItemById(internalChannelId);
                        channelName = channel?.Name;
                    }

                    return _tvDtoService.GetSeriesTimerInfoDto(i.Item1, i.Item2, channelName);
                })
                .ToArray();

            return new QueryResult<SeriesTimerInfoDto>(returnArray);
        }

        public void AddChannelInfo(IReadOnlyCollection<(BaseItemDto ItemDto, LiveTvChannel Channel)> items, DtoOptions options, User user)
        {
            DateTime now = DateTime.UtcNow;

            Guid[] channelIds = items.Select(i => i.Channel.Id).Distinct().ToArray();

            IReadOnlyList<BaseItem> programs = options.AddCurrentProgram ? _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.LiveTvProgram],
                ChannelIds = channelIds,
                MaxStartDate = now,
                MinEndDate = now,
                Limit = channelIds.Length,
                OrderBy = [(ItemSortBy.StartDate, SortOrder.Ascending)],
                TopParentIds = [GetInternalLiveTvFolder(CancellationToken.None).Id],
                DtoOptions = options
            }) : new List<BaseItem>();

            RemoveFields(options);

            List<BaseItem> currentProgramsList = new List<BaseItem>();
            Dictionary<Guid, BaseItemDto> currentChannelsDict = new Dictionary<Guid, BaseItemDto>();

            bool addCurrentProgram = options.AddCurrentProgram;

            foreach ((BaseItemDto dto, LiveTvChannel channel) in items)
            {
                dto.Number = channel.Number;
                dto.ChannelNumber = channel.Number;
                dto.ChannelType = channel.ChannelType;

                currentChannelsDict[dto.Id] = dto;

                if (addCurrentProgram)
                {
                    BaseItem currentProgram = programs.FirstOrDefault(i => channel.Id.Equals(i.ChannelId));

                    if (currentProgram is not null)
                    {
                        currentProgramsList.Add(currentProgram);
                    }
                }
            }

            if (addCurrentProgram)
            {
                IReadOnlyList<BaseItemDto> currentProgramDtos = _dtoService.GetBaseItemDtos(currentProgramsList, options, user);

                foreach (BaseItemDto programDto in currentProgramDtos)
                {
                    if (programDto.ChannelId.HasValue && currentChannelsDict.TryGetValue(programDto.ChannelId.Value, out BaseItemDto channelDto))
                    {
                        channelDto.CurrentProgram = programDto;
                    }
                }
            }
        }

        private async Task<Tuple<SeriesTimerInfo, ILiveTvService>> GetNewTimerDefaultsInternal(CancellationToken cancellationToken, LiveTvProgram program = null)
        {
            ILiveTvService service = null;
            ProgramInfo programInfo = null;

            if (program is not null)
            {
                service = GetService(program);

                BaseItem channel = _libraryManager.GetItemById(program.ChannelId);

                programInfo = new ProgramInfo
                {
                    Audio = program.Audio,
                    ChannelId = channel.ExternalId,
                    CommunityRating = program.CommunityRating,
                    EndDate = program.EndDate ?? DateTime.MinValue,
                    EpisodeTitle = program.EpisodeTitle,
                    Genres = program.Genres.ToList(),
                    Id = program.ExternalId,
                    IsHD = program.IsHD,
                    IsKids = program.IsKids,
                    IsLive = program.IsLive,
                    IsMovie = program.IsMovie,
                    IsNews = program.IsNews,
                    IsPremiere = program.IsPremiere,
                    IsRepeat = program.IsRepeat,
                    IsSeries = program.IsSeries,
                    IsSports = program.IsSports,
                    OriginalAirDate = program.PremiereDate,
                    Overview = program.Overview,
                    StartDate = program.StartDate,
                    // ImagePath = program.ExternalImagePath,
                    Name = program.Name,
                    OfficialRating = program.OfficialRating
                };
            }

            service ??= _services[0];

            SeriesTimerInfo info = await service.GetNewTimerDefaultsAsync(cancellationToken, programInfo).ConfigureAwait(false);

            info.RecordAnyTime = true;
            info.Days = new List<DayOfWeek>
            {
                DayOfWeek.Sunday,
                DayOfWeek.Monday,
                DayOfWeek.Tuesday,
                DayOfWeek.Wednesday,
                DayOfWeek.Thursday,
                DayOfWeek.Friday,
                DayOfWeek.Saturday
            };

            info.Id = null;

            return new Tuple<SeriesTimerInfo, ILiveTvService>(info, service);
        }

        public async Task<SeriesTimerInfoDto> GetNewTimerDefaults(CancellationToken cancellationToken)
        {
            Tuple<SeriesTimerInfo, ILiveTvService> info = await GetNewTimerDefaultsInternal(cancellationToken).ConfigureAwait(false);

            return _tvDtoService.GetSeriesTimerInfoDto(info.Item1, info.Item2, null);
        }

        public async Task<SeriesTimerInfoDto> GetNewTimerDefaults(string programId, CancellationToken cancellationToken)
        {
            LiveTvProgram program = (LiveTvProgram)_libraryManager.GetItemById(programId);
            BaseItemDto programDto = await GetProgram(programId, cancellationToken).ConfigureAwait(false);

            Tuple<SeriesTimerInfo, ILiveTvService> defaults = await GetNewTimerDefaultsInternal(cancellationToken, program).ConfigureAwait(false);
            SeriesTimerInfoDto info = _tvDtoService.GetSeriesTimerInfoDto(defaults.Item1, defaults.Item2, null);

            info.Days = defaults.Item1.Days.ToArray();

            info.DayPattern = _tvDtoService.GetDayPattern(info.Days);

            info.Name = program.Name;
            info.ChannelId = programDto.ChannelId ?? Guid.Empty;
            info.ChannelName = programDto.ChannelName;
            info.StartDate = program.StartDate;
            info.Name = program.Name;
            info.Overview = program.Overview;
            info.ProgramId = programDto.Id.ToString("N", CultureInfo.InvariantCulture);
            info.ExternalProgramId = program.ExternalId;

            if (program.EndDate.HasValue)
            {
                info.EndDate = program.EndDate.Value;
            }

            return info;
        }

        public async Task CreateTimer(TimerInfoDto timer, CancellationToken cancellationToken)
        {
            ILiveTvService service = GetService(timer.ServiceName);

            TimerInfo info = await _tvDtoService.GetTimerInfo(timer, true, this, cancellationToken).ConfigureAwait(false);

            // Set priority from default values
            SeriesTimerInfo defaultValues = await service.GetNewTimerDefaultsAsync(cancellationToken).ConfigureAwait(false);
            info.Priority = defaultValues.Priority;

            string newTimerId = null;
            if (service is ISupportsNewTimerIds supportsNewTimerIds)
            {
                newTimerId = await supportsNewTimerIds.CreateTimer(info, cancellationToken).ConfigureAwait(false);
                newTimerId = _tvDtoService.GetInternalTimerId(newTimerId);
            }
            else
            {
                await service.CreateTimerAsync(info, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation("New recording scheduled");

            if (service is not DefaultLiveTvService)
            {
                TimerCreated?.Invoke(this, new GenericEventArgs<TimerEventInfo>(
                    new TimerEventInfo(newTimerId)
                    {
                        ProgramId = _tvDtoService.GetInternalProgramId(info.ProgramId)
                    }));
            }
        }

        public async Task CreateSeriesTimer(SeriesTimerInfoDto timer, CancellationToken cancellationToken)
        {
            ILiveTvService service = GetService(timer.ServiceName);

            SeriesTimerInfo info = await _tvDtoService.GetSeriesTimerInfo(timer, true, this, cancellationToken).ConfigureAwait(false);

            // Set priority from default values
            SeriesTimerInfo defaultValues = await service.GetNewTimerDefaultsAsync(cancellationToken).ConfigureAwait(false);
            info.Priority = defaultValues.Priority;

            string newTimerId = null;
            if (service is ISupportsNewTimerIds supportsNewTimerIds)
            {
                newTimerId = await supportsNewTimerIds.CreateSeriesTimer(info, cancellationToken).ConfigureAwait(false);
                newTimerId = _tvDtoService.GetInternalSeriesTimerId(newTimerId).ToString("N", CultureInfo.InvariantCulture);
            }
            else
            {
                await service.CreateSeriesTimerAsync(info, cancellationToken).ConfigureAwait(false);
            }

            SeriesTimerCreated?.Invoke(this, new GenericEventArgs<TimerEventInfo>(
                new TimerEventInfo(newTimerId)
                {
                    ProgramId = _tvDtoService.GetInternalProgramId(info.ProgramId)
                }));
        }

        public async Task UpdateTimer(TimerInfoDto timer, CancellationToken cancellationToken)
        {
            TimerInfo info = await _tvDtoService.GetTimerInfo(timer, false, this, cancellationToken).ConfigureAwait(false);

            ILiveTvService service = GetService(timer.ServiceName);

            await service.UpdateTimerAsync(info, cancellationToken).ConfigureAwait(false);
        }

        public async Task UpdateSeriesTimer(SeriesTimerInfoDto timer, CancellationToken cancellationToken)
        {
            SeriesTimerInfo info = await _tvDtoService.GetSeriesTimerInfo(timer, false, this, cancellationToken).ConfigureAwait(false);

            ILiveTvService service = GetService(timer.ServiceName);

            await service.UpdateSeriesTimerAsync(info, cancellationToken).ConfigureAwait(false);
        }

        private LiveTvServiceInfo[] GetServiceInfos()
        {
            return Services.Select(GetServiceInfo).ToArray();
        }

        private static LiveTvServiceInfo GetServiceInfo(ILiveTvService service)
        {
            return new LiveTvServiceInfo
            {
                Name = service.Name
            };
        }

        public LiveTvInfo GetLiveTvInfo(CancellationToken cancellationToken)
        {
            LiveTvServiceInfo[] services = GetServiceInfos();

            LiveTvInfo info = new LiveTvInfo
            {
                Services = services,
                IsEnabled = services.Length > 0,
                EnabledUsers = _userManager.GetUsers()
                    .Where(IsLiveTvEnabled)
                    .Select(i => i.Id.ToString("N", CultureInfo.InvariantCulture))
                    .ToArray()
            };

            return info;
        }

        private bool IsLiveTvEnabled(User user)
        {
            return user.HasPermission(PermissionKind.EnableLiveTvAccess) && (Services.Count > 1 || _config.GetLiveTvConfiguration().TunerHosts.Length > 0);
        }

        public IEnumerable<User> GetEnabledUsers()
        {
            return _userManager.GetUsers()
                .Where(IsLiveTvEnabled);
        }

        /// <summary>
        /// Resets the tuner.
        /// </summary>
        /// <param name="id">The identifier.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        public Task ResetTuner(string id, CancellationToken cancellationToken)
        {
            string[] parts = id.Split('_', 2);

            ILiveTvService service = _services.FirstOrDefault(i => string.Equals(i.GetType().FullName.GetMD5().ToString("N", CultureInfo.InvariantCulture), parts[0], StringComparison.OrdinalIgnoreCase));

            if (service is null)
            {
                throw new ArgumentException("Service not found.");
            }

            return service.ResetTuner(parts[1], cancellationToken);
        }

        private static void RemoveFields(DtoOptions options)
        {
            List<ItemFields> fields = options.Fields.ToList();

            fields.Remove(ItemFields.CanDelete);
            fields.Remove(ItemFields.CanDownload);
            fields.Remove(ItemFields.DisplayPreferencesId);
            fields.Remove(ItemFields.Etag);
            options.Fields = fields.ToArray();
        }

        public Folder GetInternalLiveTvFolder(CancellationToken cancellationToken)
        {
            string name = _localization.GetLocalizedString("HeaderLiveTV");
            return _libraryManager.GetNamedView(name, CollectionType.livetv, name);
        }

        /// <inheritdoc />
        public Task<BaseItem[]> GetRecordingFoldersAsync(User user)
            => GetRecordingFoldersAsync(user, false);

        private async Task<BaseItem[]> GetRecordingFoldersAsync(User user, bool refreshChannels)
        {
            List<Folder> folders = _recordingsManager.GetRecordingFolders()
                .SelectMany(i => i.Locations)
                .Distinct()
                .Select(i => _libraryManager.FindByPath(i, true))
                .Where(i => i is not null && i.IsVisibleStandalone(user))
                .SelectMany(i => _libraryManager.GetCollectionFolders(i))
                .DistinctBy(x => x.Id)
                .OrderBy(i => i.SortName)
                .ToList();

            QueryResult<Channel> channels = await _channelManager.GetChannelsInternalAsync(new MediaBrowser.Model.Channels.ChannelQuery
            {
                UserId = user.Id,
                IsRecordingsFolder = true,
                RefreshLatestChannelItems = refreshChannels
            }).ConfigureAwait(false);

            folders.AddRange(channels.Items);

            return folders.Cast<BaseItem>().ToArray();
        }
    }
}
