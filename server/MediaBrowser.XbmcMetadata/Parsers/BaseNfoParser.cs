using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Extensions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Providers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Extensions;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using MediaBrowser.XbmcMetadata.Configuration;
using MediaBrowser.XbmcMetadata.Savers;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.XbmcMetadata.Parsers
{
    /// <summary>
    /// The BaseNfoParser class.
    /// </summary>
    /// <typeparam name="T">The type.</typeparam>
    public class BaseNfoParser<T>
        where T : BaseItem
    {
        private readonly IConfigurationManager _config;
        private readonly IUserManager _userManager;
        private readonly IUserDataManager _userDataManager;
        private readonly IDirectoryService _directoryService;
        private Dictionary<string, string> _validProviderIds;

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseNfoParser{T}" /> class.
        /// </summary>
        /// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
        /// <param name="config">Instance of the <see cref="IConfigurationManager"/> interface.</param>
        /// <param name="providerManager">Instance of the <see cref="IProviderManager"/> interface.</param>
        /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
        /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
        /// <param name="directoryService">Instance of the <see cref="IDirectoryService"/> interface.</param>
        public BaseNfoParser(
            ILogger logger,
            IConfigurationManager config,
            IProviderManager providerManager,
            IUserManager userManager,
            IUserDataManager userDataManager,
            IDirectoryService directoryService)
        {
            Logger = logger;
            _config = config;
            ProviderManager = providerManager;
            _validProviderIds = new Dictionary<string, string>();
            _userManager = userManager;
            _userDataManager = userDataManager;
            _directoryService = directoryService;
        }

        /// <summary>
        /// Gets the logger.
        /// </summary>
        protected ILogger Logger { get; }

        /// <summary>
        /// Gets the provider manager.
        /// </summary>
        protected IProviderManager ProviderManager { get; }

        /// <summary>
        /// Gets a value indicating whether URLs after a closing XML tag are supported.
        /// </summary>
        protected virtual bool SupportsUrlAfterClosingXmlTag => false;

        /// <summary>
        /// Fetches metadata for an item from one xml file.
        /// </summary>
        /// <param name="item">The <see cref="MetadataResult{T}"/>.</param>
        /// <param name="metadataFile">The metadata file.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/>.</param>
        /// <exception cref="ArgumentNullException"><c>item</c> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"><c>metadataFile</c> is <c>null</c> or empty.</exception>
        public void Fetch(MetadataResult<T> item, string metadataFile, CancellationToken cancellationToken)
        {
            if (item.Item is null)
            {
                throw new ArgumentException("Item can't be null.", nameof(item));
            }

            ArgumentException.ThrowIfNullOrEmpty(metadataFile);

            _validProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            IEnumerable<ExternalIdInfo> idInfos = ProviderManager.GetExternalIdInfos(item.Item);

            foreach (ExternalIdInfo? info in idInfos)
            {
                string id = info.Key + "Id";
                _validProviderIds.TryAdd(id, info.Key);
            }

            // Additional Mappings
            _validProviderIds.Add("collectionnumber", "TmdbCollection");
            _validProviderIds.Add("tmdbcolid", "TmdbCollection");
            _validProviderIds.Add("tmdbcol", "TmdbCollection");
            _validProviderIds.Add("imdb_id", "Imdb");

            Fetch(item, metadataFile, GetXmlReaderSettings(), cancellationToken);
        }

        /// <summary>
        /// Fetches the specified item.
        /// </summary>
        /// <param name="item">The <see cref="MetadataResult{T}"/>.</param>
        /// <param name="metadataFile">The metadata file.</param>
        /// <param name="settings">The <see cref="XmlReaderSettings"/>.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/>.</param>
        protected virtual void Fetch(MetadataResult<T> item, string metadataFile, XmlReaderSettings settings, CancellationToken cancellationToken)
        {
            if (!SupportsUrlAfterClosingXmlTag)
            {
                using (FileStream fileStream = File.OpenRead(metadataFile))
                using (StreamReader streamReader = new StreamReader(fileStream, Encoding.UTF8))
                using (XmlReader reader = XmlReader.Create(streamReader, settings))
                {
                    item.ResetPeople();

                    reader.MoveToContent();
                    reader.Read();

                    // Loop through each element
                    while (!reader.EOF && reader.ReadState == ReadState.Interactive)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (reader.NodeType == XmlNodeType.Element)
                        {
                            FetchDataFromXmlNode(reader, item);
                        }
                        else
                        {
                            reader.Read();
                        }
                    }
                }

                return;
            }

            item.ResetPeople();

            // Need to handle a url after the xml data
            // http://kodi.wiki/view/NFO_files/movies

            string xml = File.ReadAllText(metadataFile);

            // Find last closing Tag
            // Need to do this in two steps to account for random > characters after the closing xml
            int index = xml.LastIndexOf("</", StringComparison.Ordinal);

            // If closing tag exists, move to end of Tag
            if (index != -1)
            {
                index = xml.IndexOf('>', index);
            }

            if (index != -1)
            {
                ReadOnlySpan<char> endingXml = xml.AsSpan().Slice(index);

                ParseProviderLinks(item.Item, endingXml);

                // If the file is just an IMDb url, don't go any further
                if (index == 0)
                {
                    return;
                }

                xml = xml.Substring(0, index + 1);
            }
            else
            {
                // If the file is just provider urls, handle that
                ParseProviderLinks(item.Item, xml);

                return;
            }

            // These are not going to be valid xml so no sense in causing the provider to fail and spamming the log with exceptions
            try
            {
                using (StringReader stringReader = new StringReader(xml))
                using (XmlReader reader = XmlReader.Create(stringReader, settings))
                {
                    reader.MoveToContent();
                    reader.Read();

                    // Loop through each element
                    while (!reader.EOF && reader.ReadState == ReadState.Interactive)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (reader.NodeType == XmlNodeType.Element)
                        {
                            FetchDataFromXmlNode(reader, item);
                        }
                        else
                        {
                            reader.Read();
                        }
                    }
                }
            }
            catch (XmlException)
            {
            }
        }

        /// <summary>
        /// Parses a XML tag to a provider id.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="xml">The xml tag.</param>
        protected void ParseProviderLinks(T item, ReadOnlySpan<char> xml)
        {
            if (ProviderIdParsers.TryFindImdbId(xml, out ReadOnlySpan<char> imdbId))
            {
                item.SetProviderId(MetadataProvider.Imdb, imdbId.ToString());
            }

            if (item is Movie)
            {
                if (ProviderIdParsers.TryFindTmdbMovieId(xml, out ReadOnlySpan<char> tmdbId))
                {
                    item.SetProviderId(MetadataProvider.Tmdb, tmdbId.ToString());
                }
            }

            if (item is Series)
            {
                if (ProviderIdParsers.TryFindTmdbSeriesId(xml, out ReadOnlySpan<char> tmdbId))
                {
                    item.SetProviderId(MetadataProvider.Tmdb, tmdbId.ToString());
                }

                if (ProviderIdParsers.TryFindTvdbId(xml, out ReadOnlySpan<char> tvdbId))
                {
                    item.SetProviderId(MetadataProvider.Tvdb, tvdbId.ToString());
                }
            }
        }

        /// <summary>
        /// Fetches metadata from an XML node.
        /// </summary>
        /// <param name="reader">The <see cref="XmlReader"/>.</param>
        /// <param name="itemResult">The <see cref="MetadataResult{T}"/>.</param>
        protected virtual void FetchDataFromXmlNode(XmlReader reader, MetadataResult<T> itemResult)
        {
            T item = itemResult.Item;
            XbmcMetadataOptions nfoConfiguration = _config.GetNfoConfiguration();
            UserItemData? userData;

            switch (reader.Name)
            {
                case "dateadded":
                    if (reader.TryReadDateTime(out DateTime dateCreated))
                    {
                        item.DateCreated = dateCreated;
                    }

                    break;
                case "originaltitle":
                    item.OriginalTitle = reader.ReadNormalizedString();
                    break;
                case "name":
                case "title":
                case "localtitle":
                    item.Name = reader.ReadNormalizedString();
                    break;
                case "sortname":
                    item.SortName = reader.ReadNormalizedString();
                    break;
                case "criticrating":
                    string criticRatingText = reader.ReadElementContentAsString();
                    if (float.TryParse(criticRatingText, CultureInfo.InvariantCulture, out float value))
                    {
                        item.CriticRating = value;
                    }

                    break;
                case "sorttitle":
                    item.ForcedSortName = reader.ReadNormalizedString();
                    break;
                case "biography":
                case "plot":
                case "review":
                    item.Overview = reader.ReadNormalizedString();
                    break;
                case "language":
                    item.PreferredMetadataLanguage = reader.ReadNormalizedString();
                    break;
                case "watched":
                    bool played = reader.ReadElementContentAsBoolean();
                    if (Guid.TryParse(nfoConfiguration.UserId, out Guid userId))
                    {
                        User? user = _userManager.GetUserById(userId);
                        if (user is not null)
                        {
                            userData = _userDataManager.GetUserData(user, item);
                            if (userData is not null)
                            {
                                userData.Played = played;

                                if (!item.Id.IsEmpty())
                                {
                                    _userDataManager.SaveUserData(user, item, userData, UserDataSaveReason.Import, CancellationToken.None);
                                }
                            }
                        }
                    }

                    break;
                case "playcount":
                    if (reader.TryReadInt(out int count)
                        && Guid.TryParse(nfoConfiguration.UserId, out Guid playCountUserId))
                    {
                        User? user = _userManager.GetUserById(playCountUserId);
                        if (user is not null)
                        {
                            userData = _userDataManager.GetUserData(user, item);
                            if (userData is not null)
                            {
                                userData.PlayCount = count;

                                if (!item.Id.IsEmpty())
                                {
                                    _userDataManager.SaveUserData(user, item, userData, UserDataSaveReason.Import, CancellationToken.None);
                                }
                            }
                        }
                    }

                    break;
                case "lastplayed":
                    if (reader.TryReadDateTime(out DateTime lastPlayed)
                        && Guid.TryParse(nfoConfiguration.UserId, out Guid lastPlayedUserId))
                    {
                        User? user = _userManager.GetUserById(lastPlayedUserId);
                        if (user is not null)
                        {
                            userData = _userDataManager.GetUserData(user, item);
                            if (userData is not null)
                            {
                                userData.LastPlayedDate = lastPlayed;

                                if (!item.Id.IsEmpty())
                                {
                                    _userDataManager.SaveUserData(user, item, userData, UserDataSaveReason.Import, CancellationToken.None);
                                }
                            }
                        }
                    }

                    break;
                case "countrycode":
                    item.PreferredMetadataCountryCode = reader.ReadNormalizedString();
                    break;
                case "lockedfields":
                    {
                        string val = reader.ReadElementContentAsString();

                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            item.LockedFields = val.Split('|').Select(i =>
                            {
                                if (Enum.TryParse(i, true, out MetadataField field))
                                {
                                    return (MetadataField?)field;
                                }

                                return null;
                            }).OfType<MetadataField>().ToArray();
                        }

                        break;
                    }

                case "tagline":
                    item.Tagline = reader.ReadNormalizedString();
                    break;
                case "country":
                    {
                        string val = reader.ReadElementContentAsString();

                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            item.ProductionLocations = val.Split('/')
                                .Select(i => i.Trim())
                                .Where(i => !string.IsNullOrWhiteSpace(i))
                                .ToArray();
                        }

                        break;
                    }

                case "mpaa":
                    item.OfficialRating = reader.ReadNormalizedString();
                    break;
                case "customrating":
                    item.CustomRating = reader.ReadNormalizedString();
                    break;
                case "runtime":
                    string runtimeText = reader.ReadElementContentAsString();
                    if (int.TryParse(runtimeText.AsSpan().LeftPart(' '), NumberStyles.Integer, CultureInfo.InvariantCulture, out int runtime))
                    {
                        item.RunTimeTicks = TimeSpan.FromMinutes(runtime).Ticks;
                    }

                    break;
                case "aspectratio":
                    string aspectRatio = reader.ReadNormalizedString();
                    if (!string.IsNullOrEmpty(aspectRatio) && item is IHasAspectRatio hasAspectRatio)
                    {
                        hasAspectRatio.AspectRatio = aspectRatio;
                    }

                    break;
                case "lockdata":
                    item.IsLocked = string.Equals(reader.ReadElementContentAsString(), "true", StringComparison.OrdinalIgnoreCase);
                    break;
                case "studio":
                    string studio = reader.ReadNormalizedString();
                    if (!string.IsNullOrEmpty(studio))
                    {
                        item.AddStudio(studio);
                    }

                    break;
                case "director":
                    foreach (PersonInfo director in reader.GetPersonArray(PersonKind.Director))
                    {
                        itemResult.AddPerson(director);
                    }

                    break;
                case "credits":
                    {
                        string val = reader.ReadElementContentAsString();

                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            IEnumerable<string> parts = val.Split('/').Select(i => i.Trim())
                                .Where(i => !string.IsNullOrEmpty(i));

                            foreach (PersonInfo? p in parts.Select(v => new PersonInfo { Name = v.Trim(), Type = PersonKind.Writer }))
                            {
                                if (string.IsNullOrWhiteSpace(p.Name))
                                {
                                    continue;
                                }

                                itemResult.AddPerson(p);
                            }
                        }

                        break;
                    }

                case "writer":
                    foreach (PersonInfo writer in reader.GetPersonArray(PersonKind.Writer))
                    {
                        itemResult.AddPerson(writer);
                    }

                    break;
                case "actor":
                    PersonInfo? person = reader.GetPersonFromXmlNode();
                    if (person is not null)
                    {
                        itemResult.AddPerson(person);
                    }

                    break;
                case "trailer":
                    string trailer = reader.ReadNormalizedString();
                    if (!string.IsNullOrEmpty(trailer))
                    {
                        if (trailer.StartsWith("plugin://plugin.video.youtube/?action=play_video&videoid=", StringComparison.OrdinalIgnoreCase))
                        {
                            // Deprecated format
                            item.AddTrailerUrl(trailer.Replace(
                                "plugin://plugin.video.youtube/?action=play_video&videoid=",
                                BaseNfoSaver.YouTubeWatchUrl,
                                StringComparison.OrdinalIgnoreCase));

                            string suggestedUrl = trailer.Replace(
                                "plugin://plugin.video.youtube/?action=play_video&videoid=",
                                "plugin://plugin.video.youtube/play/?video_id=",
                                StringComparison.OrdinalIgnoreCase);
                            Logger.LogWarning("Trailer URL uses a deprecated format : {Url}. Using {NewUrl} instead is advised.", trailer, suggestedUrl);
                        }
                        else if (trailer.StartsWith("plugin://plugin.video.youtube/play/?video_id=", StringComparison.OrdinalIgnoreCase))
                        {
                            // Proper format
                            item.AddTrailerUrl(trailer.Replace(
                                "plugin://plugin.video.youtube/play/?video_id=",
                                BaseNfoSaver.YouTubeWatchUrl,
                                StringComparison.OrdinalIgnoreCase));
                        }
                    }

                    break;
                case "displayorder":
                    string displayOrder = reader.ReadNormalizedString();
                    if (!string.IsNullOrEmpty(displayOrder) && item is IHasDisplayOrder hasDisplayOrder)
                    {
                        hasDisplayOrder.DisplayOrder = displayOrder;
                    }

                    break;
                case "year":
                    if (reader.TryReadInt(out int productionYear) && productionYear > 1850)
                    {
                        item.ProductionYear = productionYear;
                    }

                    break;
                case "rating":
                    string rating = reader.ReadElementContentAsString().Replace(',', '.');
                    // All external meta is saving this as '.' for decimal I believe...but just to be sure
                    if (float.TryParse(rating, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out float communityRating))
                    {
                        item.CommunityRating = communityRating;
                    }

                    break;
                case "ratings":
                    FetchFromRatingsNode(reader, item);
                    break;
                case "aired":
                case "formed":
                case "premiered":
                case "releasedate":
                    if (reader.TryReadDateTimeExact(nfoConfiguration.ReleaseDateFormat, out DateTime releaseDate))
                    {
                        item.PremiereDate = releaseDate;

                        // Production year can already be set by the year tag
                        item.ProductionYear ??= releaseDate.Year;
                    }

                    break;
                case "enddate":
                    if (reader.TryReadDateTimeExact(nfoConfiguration.ReleaseDateFormat, out DateTime endDate))
                    {
                        item.EndDate = endDate;
                    }

                    break;
                case "genre":
                    {
                        string val = reader.ReadElementContentAsString();

                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            IEnumerable<string> parts = val.Split('/')
                                .Select(i => i.Trim())
                                .Where(i => !string.IsNullOrWhiteSpace(i));

                            foreach (string? p in parts)
                            {
                                item.AddGenre(p);
                            }
                        }

                        break;
                    }

                case "style":
                case "tag":
                    string tag = reader.ReadNormalizedString();
                    if (!string.IsNullOrEmpty(tag))
                    {
                        item.AddTag(tag);
                    }

                    break;
                case "fileinfo":
                    FetchFromFileInfoNode(reader, item);
                    break;
                case "uniqueid":
                    if (reader.IsEmptyElement)
                    {
                        reader.Read();
                        break;
                    }

                    string? provider = reader.GetAttribute("type");
                    string providerId = reader.ReadElementContentAsString();

                    if (!string.IsNullOrEmpty(provider))
                    {
                        if (_validProviderIds.TryGetValue(provider, out string? normalizedProvider))
                        {
                            item.TrySetProviderId(normalizedProvider, providerId);
                        }
                        else
                        {
                            item.TrySetProviderId(provider, providerId);
                        }
                    }

                    break;
                case "thumb":
                    FetchThumbNode(reader, itemResult, "thumb");
                    break;
                case "fanart":
                    {
                        if (reader.IsEmptyElement)
                        {
                            reader.Read();
                            break;
                        }

                        using XmlReader subtree = reader.ReadSubtree();
                        if (!subtree.ReadToDescendant("thumb"))
                        {
                            break;
                        }

                        FetchThumbNode(subtree, itemResult, "fanart");
                        break;
                    }

                default:
                    string readerName = reader.Name;
                    if (_validProviderIds.TryGetValue(readerName, out string? providerIdValue))
                    {
                        string id = reader.ReadElementContentAsString();
                        item.TrySetProviderId(providerIdValue, id);
                    }
                    else
                    {
                        reader.Skip();
                    }

                    break;
            }
        }

        private void FetchThumbNode(XmlReader reader, MetadataResult<T> itemResult, string parentNode)
        {
            string? artType = reader.GetAttribute("aspect");
            string val = reader.ReadElementContentAsString();

            // artType is null if the thumb node is a child of the fanart tag
            // -> set image type to fanart
            if (string.IsNullOrWhiteSpace(artType) && parentNode.Equals("fanart", StringComparison.Ordinal))
            {
                artType = "fanart";
            }
            else if (string.IsNullOrWhiteSpace(artType))
            {
                // Sonarr writes thumb tags for posters without aspect property
                artType = "poster";
            }

            // skip:
            // - empty uri
            // - tag containing '.' because we can't set images for seasons, episodes or movie sets within series or movies
            if (string.IsNullOrEmpty(val) || artType.Contains('.', StringComparison.Ordinal))
            {
                return;
            }

            ImageType imageType = GetImageType(artType);

            if (!Uri.TryCreate(val, UriKind.Absolute, out Uri? uri))
            {
                Logger.LogError("Image location {Path} specified in nfo file for {ItemName} is not a valid URL or file path.", val, itemResult.Item.Name);
                return;
            }

            if (uri.IsFile)
            {
                // only allow one item of each type
                if (itemResult.Images.Any(x => x.Type == imageType))
                {
                    return;
                }

                FileSystemMetadata? fileSystemMetadata = _directoryService.GetFile(val);
                // nonexistent file returns null
                if (fileSystemMetadata is null || !fileSystemMetadata.Exists)
                {
                    Logger.LogWarning("Artwork file {Path} specified in nfo file for {ItemName} does not exist.", uri, itemResult.Item.Name);
                    return;
                }

                itemResult.Images.Add(new LocalImageInfo()
                {
                    FileInfo = fileSystemMetadata,
                    Type = imageType
                });
            }
            else
            {
                // only allow one item of each type
                if (itemResult.RemoteImages.Any(x => x.Type == imageType))
                {
                    return;
                }

                itemResult.RemoteImages.Add((uri.ToString(), imageType));
            }
        }

        private void FetchFromFileInfoNode(XmlReader parentReader, T item)
        {
            if (parentReader.IsEmptyElement)
            {
                parentReader.Read();
                return;
            }

            using XmlReader reader = parentReader.ReadSubtree();
            reader.MoveToContent();
            reader.Read();

            // Loop through each element
            while (!reader.EOF && reader.ReadState == ReadState.Interactive)
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    reader.Read();
                    continue;
                }

                switch (reader.Name)
                {
                    case "streamdetails":
                        FetchFromStreamDetailsNode(reader, item);
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }
        }

        private void FetchFromStreamDetailsNode(XmlReader parentReader, T item)
        {
            if (parentReader.IsEmptyElement)
            {
                parentReader.Read();
                return;
            }

            using XmlReader reader = parentReader.ReadSubtree();
            reader.MoveToContent();
            reader.Read();

            // Loop through each element
            while (!reader.EOF && reader.ReadState == ReadState.Interactive)
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    reader.Read();
                    continue;
                }

                switch (reader.Name)
                {
                    case "video":
                        FetchFromVideoNode(reader, item);
                        break;
                    case "subtitle":
                        FetchFromSubtitleNode(reader, item);
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }
        }

        private void FetchFromVideoNode(XmlReader parentReader, T item)
        {
            if (parentReader.IsEmptyElement)
            {
                parentReader.Read();
                return;
            }

            using XmlReader reader = parentReader.ReadSubtree();
            reader.MoveToContent();
            reader.Read();

            // Loop through each element
            while (!reader.EOF && reader.ReadState == ReadState.Interactive)
            {
                if (reader.NodeType != XmlNodeType.Element || item is not Video video)
                {
                    reader.Read();
                    continue;
                }

                switch (reader.Name)
                {
                    case "format3d":
                        string format = reader.ReadElementContentAsString();
                        if (string.Equals("HSBS", format, StringComparison.OrdinalIgnoreCase))
                        {
                            video.Video3DFormat = Video3DFormat.HalfSideBySide;
                        }
                        else if (string.Equals("HTAB", format, StringComparison.OrdinalIgnoreCase))
                        {
                            video.Video3DFormat = Video3DFormat.HalfTopAndBottom;
                        }
                        else if (string.Equals("FTAB", format, StringComparison.OrdinalIgnoreCase))
                        {
                            video.Video3DFormat = Video3DFormat.FullTopAndBottom;
                        }
                        else if (string.Equals("FSBS", format, StringComparison.OrdinalIgnoreCase))
                        {
                            video.Video3DFormat = Video3DFormat.FullSideBySide;
                        }
                        else if (string.Equals("MVC", format, StringComparison.OrdinalIgnoreCase))
                        {
                            video.Video3DFormat = Video3DFormat.MVC;
                        }

                        break;
                    case "aspect":
                        video.AspectRatio = reader.ReadNormalizedString();
                        break;
                    case "width":
                        video.Width = reader.ReadElementContentAsInt();
                        break;
                    case "height":
                        video.Height = reader.ReadElementContentAsInt();
                        break;
                    case "durationinseconds":
                        video.RunTimeTicks = new TimeSpan(0, 0, reader.ReadElementContentAsInt()).Ticks;
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }
        }

        private void FetchFromSubtitleNode(XmlReader parentReader, T item)
        {
            if (parentReader.IsEmptyElement)
            {
                parentReader.Read();
                return;
            }

            using XmlReader reader = parentReader.ReadSubtree();
            reader.MoveToContent();
            reader.Read();

            // Loop through each element
            while (!reader.EOF && reader.ReadState == ReadState.Interactive)
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    reader.Read();
                    continue;
                }

                switch (reader.Name)
                {
                    case "language":
                        _ = reader.ReadElementContentAsString();
                        if (item is Video video)
                        {
                            video.HasSubtitles = true;
                        }

                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }
        }

        private void FetchFromRatingsNode(XmlReader parentReader, T item)
        {
            if (parentReader.IsEmptyElement)
            {
                parentReader.Read();
                return;
            }

            using XmlReader reader = parentReader.ReadSubtree();
            reader.MoveToContent();
            reader.Read();

            // Loop through each element
            while (!reader.EOF && reader.ReadState == ReadState.Interactive)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    switch (reader.Name)
                    {
                        case "rating":
                            {
                                if (reader.IsEmptyElement)
                                {
                                    reader.Read();
                                    continue;
                                }

                                string? ratingName = reader.GetAttribute("name");

                                using XmlReader subtree = reader.ReadSubtree();
                                FetchFromRatingNode(subtree, item, ratingName);

                                break;
                            }

                        default:
                            reader.Skip();
                            break;
                    }
                }
                else
                {
                    reader.Read();
                }
            }
        }

        private void FetchFromRatingNode(XmlReader reader, T item, string? ratingName)
        {
            reader.MoveToContent();
            reader.Read();

            // Loop through each element
            while (!reader.EOF && reader.ReadState == ReadState.Interactive)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    switch (reader.Name)
                    {
                        case "value":
                            string val = reader.ReadElementContentAsString();

                            if (float.TryParse(val, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out float ratingValue))
                            {
                                // if ratingName contains tomato --> assume critic rating
                                if (ratingName is not null
                                    && ratingName.Contains("tomato", StringComparison.OrdinalIgnoreCase)
                                    && !ratingName.Contains("audience", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (!ratingName.Contains("avg", StringComparison.OrdinalIgnoreCase))
                                    {
                                        item.CriticRating = ratingValue;
                                    }
                                }
                                else
                                {
                                    item.CommunityRating = ratingValue;
                                }
                            }

                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }
                else
                {
                    reader.Read();
                }
            }
        }

        internal XmlReaderSettings GetXmlReaderSettings()
            => new XmlReaderSettings()
            {
                ValidationType = ValidationType.None,
                CheckCharacters = false,
                IgnoreProcessingInstructions = true,
                IgnoreComments = true
            };

        /// <summary>
        /// Parses the <see cref="ImageType"/> from the NFO aspect property.
        /// </summary>
        /// <param name="aspect">The NFO aspect property.</param>
        /// <returns>The <see cref="ImageType"/>.</returns>
        private static ImageType GetImageType(string aspect)
        {
            return aspect switch
            {
                "banner" => ImageType.Banner,
                "clearlogo" => ImageType.Logo,
                "discart" => ImageType.Disc,
                "landscape" => ImageType.Thumb,
                "clearart" => ImageType.Art,
                "fanart" => ImageType.Backdrop,
                // unknown type (including "poster") --> primary
                _ => ImageType.Primary,
            };
        }
    }
}
