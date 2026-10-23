using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using TMDbLib.Objects.Collections;
using TMDbLib.Objects.Search;

namespace MediaBrowser.Providers.Plugins.Tmdb.BoxSets
{
    /// <summary>
    /// BoxSet provider powered by TMDb.
    /// </summary>
    public class TmdbBoxSetProvider : IRemoteMetadataProvider<BoxSet, BoxSetInfo>
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly TmdbClientManager _tmdbClientManager;
        private readonly ILibraryManager _libraryManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="TmdbBoxSetProvider"/> class.
        /// </summary>
        /// <param name="libraryManager">The <see cref="ILibraryManager"/>.</param>
        /// <param name="httpClientFactory">The <see cref="IHttpClientFactory"/>.</param>
        /// <param name="tmdbClientManager">The <see cref="TmdbClientManager"/>.</param>
        public TmdbBoxSetProvider(IHttpClientFactory httpClientFactory, TmdbClientManager tmdbClientManager, ILibraryManager libraryManager)
        {
            _httpClientFactory = httpClientFactory;
            _tmdbClientManager = tmdbClientManager;
            _libraryManager = libraryManager;
        }

        /// <inheritdoc />
        public string Name => TmdbUtils.ProviderName;

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BoxSetInfo searchInfo, CancellationToken cancellationToken)
        {
            int tmdbId = Convert.ToInt32(searchInfo.GetProviderId(MetadataProvider.Tmdb), CultureInfo.InvariantCulture);
            string language = searchInfo.MetadataLanguage;

            if (tmdbId > 0)
            {
                Collection? collection = await _tmdbClientManager.GetCollectionAsync(tmdbId, language, TmdbUtils.GetImageLanguagesParam(language, searchInfo.MetadataCountryCode), searchInfo.MetadataCountryCode, cancellationToken).ConfigureAwait(false);

                if (collection is null)
                {
                    return Enumerable.Empty<RemoteSearchResult>();
                }

                RemoteSearchResult result = new RemoteSearchResult
                {
                    Name = collection.Name,
                    SearchProviderName = Name
                };

                if (collection.Images is not null)
                {
                    result.ImageUrl = _tmdbClientManager.GetPosterUrl(collection.PosterPath);
                }

                result.SetProviderId(MetadataProvider.Tmdb, collection.Id.ToString(CultureInfo.InvariantCulture));

                return new[] { result };
            }

            IReadOnlyList<SearchCollection> collectionSearchResults = await _tmdbClientManager.SearchCollectionAsync(searchInfo.Name, language, searchInfo.MetadataCountryCode, cancellationToken).ConfigureAwait(false);

            RemoteSearchResult[] collections = new RemoteSearchResult[collectionSearchResults.Count];
            for (int i = 0; i < collectionSearchResults.Count; i++)
            {
                SearchCollection result = collectionSearchResults[i];
                RemoteSearchResult collection = new RemoteSearchResult
                {
                    Name = result.Name,
                    SearchProviderName = Name,
                    ImageUrl = _tmdbClientManager.GetPosterUrl(result.PosterPath)
                };
                collection.SetProviderId(MetadataProvider.Tmdb, result.Id.ToString(CultureInfo.InvariantCulture));

                collections[i] = collection;
            }

            return collections;
        }

        /// <inheritdoc />
        public async Task<MetadataResult<BoxSet>> GetMetadata(BoxSetInfo info, CancellationToken cancellationToken)
        {
            int tmdbId = Convert.ToInt32(info.GetProviderId(MetadataProvider.Tmdb), CultureInfo.InvariantCulture);
            string language = info.MetadataLanguage;

            // We don't already have an Id, need to fetch it
            if (tmdbId <= 0)
            {
                // ParseName is required here.
                // Caller provides the filename with extension stripped and NOT the parsed filename
                ItemLookupInfo parsedName = _libraryManager.ParseName(info.Name);
                string cleanedName = TmdbUtils.CleanName(parsedName.Name);
                IReadOnlyList<SearchCollection>? searchResults = await _tmdbClientManager.SearchCollectionAsync(cleanedName, language, info.MetadataCountryCode, cancellationToken).ConfigureAwait(false);

                if (searchResults is not null && searchResults.Count > 0)
                {
                    tmdbId = searchResults[0].Id;
                }
            }

            MetadataResult<BoxSet> result = new MetadataResult<BoxSet>();

            if (tmdbId > 0)
            {
                Collection? collection = await _tmdbClientManager.GetCollectionAsync(tmdbId, language, TmdbUtils.GetImageLanguagesParam(language, info.MetadataCountryCode), info.MetadataCountryCode, cancellationToken).ConfigureAwait(false);

                if (collection is not null)
                {
                    BoxSet item = new BoxSet
                    {
                        Name = collection.Name,
                        Overview = collection.Overview
                    };

                    item.SetProviderId(MetadataProvider.Tmdb, collection.Id.ToString(CultureInfo.InvariantCulture));

                    result.HasMetadata = true;
                    result.Item = item;
                }
            }

            return result;
        }

        /// <inheritdoc />
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);
        }
    }
}
