using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using TMDbLib.Objects.Find;
using TMDbLib.Objects.General;

namespace MediaBrowser.Providers.Plugins.Tmdb.Movies
{
    /// <summary>
    /// Movie image provider powered by TMDb.
    /// </summary>
    public class TmdbMovieImageProvider : IRemoteImageProvider, IHasOrder
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly TmdbClientManager _tmdbClientManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="TmdbMovieImageProvider"/> class.
        /// </summary>
        /// <param name="httpClientFactory">The <see cref="IHttpClientFactory"/>.</param>
        /// <param name="tmdbClientManager">The <see cref="TmdbClientManager"/>.</param>
        public TmdbMovieImageProvider(IHttpClientFactory httpClientFactory, TmdbClientManager tmdbClientManager)
        {
            _httpClientFactory = httpClientFactory;
            _tmdbClientManager = tmdbClientManager;
        }

        /// <inheritdoc />
        public int Order => 0;

        /// <inheritdoc />
        public string Name => TmdbUtils.ProviderName;

        /// <inheritdoc />
        public bool Supports(BaseItem item)
        {
            return item is Movie || item is Trailer;
        }

        /// <inheritdoc />
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) =>
        [
            ImageType.Primary,
            ImageType.Backdrop,
            ImageType.Logo,
            ImageType.Thumb
        ];

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            string language = item.GetPreferredMetadataLanguage();
            string countryCode = item.GetPreferredMetadataCountryCode();

            int movieTmdbId = Convert.ToInt32(item.GetProviderId(MetadataProvider.Tmdb), CultureInfo.InvariantCulture);
            if (movieTmdbId <= 0)
            {
                string? movieImdbId = item.GetProviderId(MetadataProvider.Imdb);
                if (string.IsNullOrEmpty(movieImdbId))
                {
                    return Enumerable.Empty<RemoteImageInfo>();
                }

                FindContainer? movieResult = await _tmdbClientManager.FindByExternalIdAsync(movieImdbId, FindExternalSource.Imdb, language, countryCode, cancellationToken).ConfigureAwait(false);
                if (movieResult?.MovieResults is not null && movieResult.MovieResults.Count > 0)
                {
                    movieTmdbId = movieResult.MovieResults[0].Id;
                }
            }

            if (movieTmdbId <= 0)
            {
                return Enumerable.Empty<RemoteImageInfo>();
            }

            // TODO use image languages if All Languages isn't toggled, but there's currently no way to get that value in here
            TMDbLib.Objects.Movies.Movie? movie = await _tmdbClientManager
                .GetMovieAsync(movieTmdbId, null, null, null, cancellationToken)
                .ConfigureAwait(false);

            if (movie?.Images is null)
            {
                return Enumerable.Empty<RemoteImageInfo>();
            }

            List<ImageData> posters = movie.Images.Posters;
            List<ImageData> backdrops = movie.Images.Backdrops;
            List<ImageData> logos = movie.Images.Logos;
            List<RemoteImageInfo> remoteImages = new List<RemoteImageInfo>(posters.Count + backdrops.Count + logos.Count);

            remoteImages.AddRange(_tmdbClientManager.ConvertPostersToRemoteImageInfo(posters, language));
            remoteImages.AddRange(_tmdbClientManager.ConvertBackdropsToRemoteImageInfo(backdrops, language));
            remoteImages.AddRange(_tmdbClientManager.ConvertLogosToRemoteImageInfo(logos, language));

            return remoteImages;
        }

        /// <inheritdoc />
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);
        }
    }
}
