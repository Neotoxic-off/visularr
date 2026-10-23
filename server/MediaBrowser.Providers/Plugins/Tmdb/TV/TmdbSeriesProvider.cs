using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using TMDbLib.Objects.Find;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Search;
using TMDbLib.Objects.TvShows;

namespace MediaBrowser.Providers.Plugins.Tmdb.TV
{
    /// <summary>
    /// TV series provider powered by TheMovieDb.
    /// </summary>
    public class TmdbSeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>, IHasOrder
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILibraryManager _libraryManager;
        private readonly TmdbClientManager _tmdbClientManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="TmdbSeriesProvider"/> class.
        /// </summary>
        /// <param name="libraryManager">The <see cref="ILibraryManager"/>.</param>
        /// <param name="httpClientFactory">The <see cref="IHttpClientFactory"/>.</param>
        /// <param name="tmdbClientManager">The <see cref="TmdbClientManager"/>.</param>
        public TmdbSeriesProvider(
            ILibraryManager libraryManager,
            IHttpClientFactory httpClientFactory,
            TmdbClientManager tmdbClientManager)
        {
            _libraryManager = libraryManager;
            _httpClientFactory = httpClientFactory;
            _tmdbClientManager = tmdbClientManager;
        }

        /// <inheritdoc />
        public string Name => TmdbUtils.ProviderName;

        /// <inheritdoc />
        public int Order => 1;

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        {
            if (searchInfo.TryGetProviderId(MetadataProvider.Tmdb, out string? tmdbId))
            {
                TvShow? series = await _tmdbClientManager
                    .GetSeriesAsync(Convert.ToInt32(tmdbId, CultureInfo.InvariantCulture), searchInfo.MetadataLanguage, searchInfo.MetadataLanguage, searchInfo.MetadataCountryCode, cancellationToken)
                    .ConfigureAwait(false);

                if (series is not null)
                {
                    RemoteSearchResult remoteResult = MapTvShowToRemoteSearchResult(series);

                    return new[] { remoteResult };
                }
            }

            if (searchInfo.TryGetProviderId(MetadataProvider.Imdb, out string? imdbId))
            {
                FindContainer? findResult = await _tmdbClientManager
                    .FindByExternalIdAsync(imdbId, FindExternalSource.Imdb, searchInfo.MetadataLanguage, searchInfo.MetadataCountryCode, cancellationToken)
                    .ConfigureAwait(false);

                List<SearchTv>? tvResults = findResult?.TvResults;
                if (tvResults is not null)
                {
                    RemoteSearchResult[] imdbIdResults = new RemoteSearchResult[tvResults.Count];
                    for (int i = 0; i < tvResults.Count; i++)
                    {
                        RemoteSearchResult remoteResult = MapSearchTvToRemoteSearchResult(tvResults[i]);
                        remoteResult.SetProviderId(MetadataProvider.Imdb, imdbId);
                        imdbIdResults[i] = remoteResult;
                    }

                    return imdbIdResults;
                }
            }

            if (searchInfo.TryGetProviderId(MetadataProvider.Tvdb, out string? tvdbId))
            {
                FindContainer? findResult = await _tmdbClientManager
                    .FindByExternalIdAsync(tvdbId, FindExternalSource.TvDb, searchInfo.MetadataLanguage, searchInfo.MetadataCountryCode, cancellationToken)
                    .ConfigureAwait(false);

                List<SearchTv>? tvResults = findResult?.TvResults;
                if (tvResults is not null)
                {
                    RemoteSearchResult[] tvIdResults = new RemoteSearchResult[tvResults.Count];
                    for (int i = 0; i < tvResults.Count; i++)
                    {
                        RemoteSearchResult remoteResult = MapSearchTvToRemoteSearchResult(tvResults[i]);
                        remoteResult.SetProviderId(MetadataProvider.Tvdb, tvdbId);
                        tvIdResults[i] = remoteResult;
                    }

                    return tvIdResults;
                }
            }

            IReadOnlyList<SearchTv> tvSearchResults = await _tmdbClientManager.SearchSeriesAsync(searchInfo.Name, searchInfo.MetadataLanguage, searchInfo.MetadataCountryCode, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            RemoteSearchResult[] remoteResults = new RemoteSearchResult[tvSearchResults.Count];
            for (int i = 0; i < tvSearchResults.Count; i++)
            {
                remoteResults[i] = MapSearchTvToRemoteSearchResult(tvSearchResults[i]);
            }

            return remoteResults;
        }

        private RemoteSearchResult MapTvShowToRemoteSearchResult(TvShow series)
        {
            RemoteSearchResult remoteResult = new RemoteSearchResult
            {
                Name = series.Name ?? series.OriginalName,
                SearchProviderName = Name,
                ImageUrl = _tmdbClientManager.GetPosterUrl(series.PosterPath),
                Overview = series.Overview
            };

            remoteResult.SetProviderId(MetadataProvider.Tmdb, series.Id.ToString(CultureInfo.InvariantCulture));
            if (series.ExternalIds is not null)
            {
                remoteResult.TrySetProviderId(MetadataProvider.Imdb, series.ExternalIds.ImdbId);

                remoteResult.TrySetProviderId(MetadataProvider.Tvdb, series.ExternalIds.TvdbId);
            }

            remoteResult.PremiereDate = series.FirstAirDate?.ToUniversalTime();

            return remoteResult;
        }

        private RemoteSearchResult MapSearchTvToRemoteSearchResult(SearchTv series)
        {
            RemoteSearchResult remoteResult = new RemoteSearchResult
            {
                Name = series.Name ?? series.OriginalName,
                SearchProviderName = Name,
                ImageUrl = _tmdbClientManager.GetPosterUrl(series.PosterPath),
                Overview = series.Overview
            };

            remoteResult.SetProviderId(MetadataProvider.Tmdb, series.Id.ToString(CultureInfo.InvariantCulture));
            remoteResult.PremiereDate = series.FirstAirDate?.ToUniversalTime();

            return remoteResult;
        }

        /// <inheritdoc />
        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            MetadataResult<Series> result = new MetadataResult<Series>
            {
                QueriedById = true
            };

            string? tmdbId = info.GetProviderId(MetadataProvider.Tmdb);

            if (string.IsNullOrEmpty(tmdbId) && info.TryGetProviderId(MetadataProvider.Imdb, out string? imdbId))
            {
                FindContainer? searchResult = await _tmdbClientManager.FindByExternalIdAsync(imdbId, FindExternalSource.Imdb, info.MetadataLanguage, info.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
                if (searchResult?.TvResults.Count > 0)
                {
                    tmdbId = searchResult.TvResults[0].Id.ToString(CultureInfo.InvariantCulture);
                }
            }

            if (string.IsNullOrEmpty(tmdbId) && info.TryGetProviderId(MetadataProvider.Tvdb, out string? tvdbId))
            {
                FindContainer? searchResult = await _tmdbClientManager.FindByExternalIdAsync(tvdbId, FindExternalSource.TvDb, info.MetadataLanguage, info.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
                if (searchResult?.TvResults.Count > 0)
                {
                    tmdbId = searchResult.TvResults[0].Id.ToString(CultureInfo.InvariantCulture);
                }
            }

            if (string.IsNullOrEmpty(tmdbId))
            {
                result.QueriedById = false;
                // ParseName is required here.
                // Caller provides the filename with extension stripped and NOT the parsed filename
                ItemLookupInfo parsedName = _libraryManager.ParseName(info.Name);
                string cleanedName = TmdbUtils.CleanName(parsedName.Name);
                IReadOnlyList<SearchTv> searchResults = await _tmdbClientManager.SearchSeriesAsync(cleanedName, info.MetadataLanguage, info.MetadataCountryCode, info.Year ?? parsedName.Year ?? 0, cancellationToken).ConfigureAwait(false);

                if (searchResults.Count > 0)
                {
                    tmdbId = searchResults[0].Id.ToString(CultureInfo.InvariantCulture);
                }
            }

            if (!int.TryParse(tmdbId, CultureInfo.InvariantCulture, out int tmdbIdInt))
            {
                return result;
            }

            cancellationToken.ThrowIfCancellationRequested();

            TvShow? tvShow = await _tmdbClientManager
                .GetSeriesAsync(tmdbIdInt, info.MetadataLanguage, TmdbUtils.GetImageLanguagesParam(info.MetadataLanguage, info.MetadataCountryCode), info.MetadataCountryCode, cancellationToken)
                .ConfigureAwait(false);

            if (tvShow is null)
            {
                return result;
            }

            result = new MetadataResult<Series>
            {
                Item = MapTvShowToSeries(tvShow, info.MetadataCountryCode),
                ResultLanguage = info.MetadataLanguage ?? tvShow.OriginalLanguage
            };

            foreach (PersonInfo person in GetPersons(tvShow))
            {
                result.AddPerson(person);
            }

            result.HasMetadata = result.Item is not null;

            return result;
        }

        private static Series MapTvShowToSeries(TvShow seriesResult, string preferredCountryCode)
        {
            Series series = new Series
            {
                Name = seriesResult.Name,
                OriginalTitle = seriesResult.OriginalName
            };

            series.SetProviderId(MetadataProvider.Tmdb, seriesResult.Id.ToString(CultureInfo.InvariantCulture));

            series.CommunityRating = Convert.ToSingle(seriesResult.VoteAverage);

            series.Overview = seriesResult.Overview;

            if (seriesResult.Networks is not null)
            {
                series.Studios = seriesResult.Networks.Select(i => i.Name).ToArray();
            }

            if (seriesResult.Genres is not null)
            {
                series.Genres = seriesResult.Genres.Select(i => i.Name).ToArray();
            }

            if (seriesResult.Keywords?.Results is not null)
            {
                for (int i = 0; i < seriesResult.Keywords.Results.Count; i++)
                {
                    series.AddTag(seriesResult.Keywords.Results[i].Name);
                }
            }

            series.HomePageUrl = seriesResult.Homepage;

            series.RunTimeTicks = seriesResult.EpisodeRunTime.Select(i => TimeSpan.FromMinutes(i).Ticks).FirstOrDefault();

            if (Emby.Naming.TV.TvParserHelpers.TryParseSeriesStatus(seriesResult.Status, out SeriesStatus? seriesStatus))
            {
                series.Status = seriesStatus;
            }

            series.EndDate = seriesResult.LastAirDate;
            series.PremiereDate = seriesResult.FirstAirDate;

            ExternalIdsTvShow? ids = seriesResult.ExternalIds;
            if (ids is not null)
            {
                series.TrySetProviderId(MetadataProvider.Imdb, ids.ImdbId);
                series.TrySetProviderId(MetadataProvider.TvRage, ids.TvrageId);
                series.TrySetProviderId(MetadataProvider.Tvdb, ids.TvdbId);
            }

            List<ContentRating> contentRatings = seriesResult.ContentRatings.Results ?? new List<ContentRating>();

            ContentRating? ourRelease = contentRatings.FirstOrDefault(c => string.Equals(c.Iso_3166_1, preferredCountryCode, StringComparison.OrdinalIgnoreCase));
            ContentRating? usRelease = contentRatings.FirstOrDefault(c => string.Equals(c.Iso_3166_1, "US", StringComparison.OrdinalIgnoreCase));
            ContentRating? minimumRelease = contentRatings.FirstOrDefault();

            if (ourRelease is not null)
            {
                series.OfficialRating = TmdbUtils.BuildParentalRating(ourRelease.Iso_3166_1, ourRelease.Rating);
            }
            else if (usRelease is not null)
            {
                series.OfficialRating = usRelease.Rating;
            }
            else if (minimumRelease is not null)
            {
                series.OfficialRating = minimumRelease.Rating;
            }

            if (seriesResult.Videos?.Results is not null)
            {
                foreach (TMDbLib.Objects.General.Video? video in seriesResult.Videos.Results)
                {
                    if (TmdbUtils.IsTrailerType(video))
                    {
                        series.AddTrailerUrl("https://www.youtube.com/watch?v=" + video.Key);
                    }
                }
            }

            return series;
        }

        private IEnumerable<PersonInfo> GetPersons(TvShow seriesResult)
        {
            PluginConfiguration config = Plugin.Instance.Configuration;

            if (seriesResult.Credits?.Cast is not null)
            {
                IEnumerable<Cast> castQuery = seriesResult.Credits.Cast.OrderBy(a => a.Order);

                if (config.HideMissingCastMembers)
                {
                    castQuery = castQuery.Where(a => !string.IsNullOrEmpty(a.ProfilePath));
                }

                foreach (Cast? actor in castQuery.Take(config.MaxCastMembers))
                {
                    if (string.IsNullOrWhiteSpace(actor.Name))
                    {
                        continue;
                    }

                    PersonInfo personInfo = new PersonInfo
                    {
                        Name = actor.Name.Trim(),
                        Role = actor.Character?.Trim() ?? string.Empty,
                        Type = PersonKind.Actor,
                        SortOrder = actor.Order,
                        ImageUrl = _tmdbClientManager.GetProfileUrl(actor.ProfilePath)
                    };

                    if (actor.Id > 0)
                    {
                        personInfo.SetProviderId(MetadataProvider.Tmdb, actor.Id.ToString(CultureInfo.InvariantCulture));
                    }

                    yield return personInfo;
                }
            }

            if (seriesResult.Credits?.Crew is not null)
            {
                var crewQuery = seriesResult.Credits.Crew
                    .Select(crewMember => new
                    {
                        CrewMember = crewMember,
                        PersonType = TmdbUtils.MapCrewToPersonType(crewMember)
                    })
                    .Where(entry => TmdbUtils.WantedCrewKinds.Contains(entry.PersonType));

                if (config.HideMissingCrewMembers)
                {
                    crewQuery = crewQuery.Where(entry => !string.IsNullOrEmpty(entry.CrewMember.ProfilePath));
                }

                foreach (var entry in crewQuery.Take(config.MaxCrewMembers))
                {
                    Crew crewMember = entry.CrewMember;

                    if (string.IsNullOrWhiteSpace(crewMember.Name))
                    {
                        continue;
                    }

                    PersonInfo personInfo = new PersonInfo
                    {
                        Name = crewMember.Name.Trim(),
                        Role = crewMember.Job?.Trim() ?? string.Empty,
                        Type = entry.PersonType,
                        ImageUrl = _tmdbClientManager.GetProfileUrl(crewMember.ProfilePath)
                    };

                    if (crewMember.Id > 0)
                    {
                        personInfo.SetProviderId(MetadataProvider.Tmdb, crewMember.Id.ToString(CultureInfo.InvariantCulture));
                    }

                    yield return personInfo;
                }
            }
        }

        /// <inheritdoc />
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);
        }
    }
}
