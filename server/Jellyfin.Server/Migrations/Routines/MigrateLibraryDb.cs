#pragma warning disable RS0030 // Do not use banned APIs

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Emby.Server.Implementations.Data;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Extensions;
using Jellyfin.Server.Implementations.Item;
using Jellyfin.Server.ServerSetupApp;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using BaseItemEntity = Jellyfin.Database.Implementations.Entities.BaseItemEntity;
using Chapter = Jellyfin.Database.Implementations.Entities.Chapter;

namespace Jellyfin.Server.Migrations.Routines;

/// <summary>
/// The migration routine for migrating the userdata database to EF Core.
/// </summary>
[JellyfinMigration("2025-04-20T20:00:00", nameof(MigrateLibraryDb))]
[JellyfinMigrationBackup(JellyfinDb = true, LegacyLibraryDb = true)]
internal class MigrateLibraryDb : IDatabaseMigrationRoutine
{
    private const string DbFilename = "library.db";

    private readonly IStartupLogger _logger;
    private readonly IServerApplicationPaths _paths;
    private readonly IJellyfinDatabaseProvider _jellyfinDatabaseProvider;
    private readonly IDbContextFactory<JellyfinDbContext> _provider;

    /// <summary>
    /// Initializes a new instance of the <see cref="MigrateLibraryDb"/> class.
    /// </summary>
    /// <param name="startupLogger">The startup logger for Startup UI intigration.</param>
    /// <param name="provider">The database provider.</param>
    /// <param name="paths">The server application paths.</param>
    /// <param name="jellyfinDatabaseProvider">The database provider for special access.</param>
    public MigrateLibraryDb(
        IStartupLogger<MigrateLibraryDb> startupLogger,
        IDbContextFactory<JellyfinDbContext> provider,
        IServerApplicationPaths paths,
        IJellyfinDatabaseProvider jellyfinDatabaseProvider)
    {
        _logger = startupLogger;
        _provider = provider;
        _paths = paths;
        _jellyfinDatabaseProvider = jellyfinDatabaseProvider;
    }

    /// <inheritdoc/>
    public void Perform()
    {
        _logger.LogInformation("Migrating the userdata from library.db may take a while, do not stop Jellyfin.");

        string dataPath = _paths.DataPath;
        string libraryDbPath = Path.Combine(dataPath, DbFilename);
        if (!File.Exists(libraryDbPath))
        {
            _logger.LogError("Cannot migrate {LibraryDb} as it does not exist..", libraryDbPath);
            return;
        }

        using SqliteConnection connection = new SqliteConnection($"Filename={libraryDbPath};Mode=ReadOnly");

        Stopwatch fullOperationTimer = new Stopwatch();
        fullOperationTimer.Start();

        using (DatabaseMigrationStep operation = GetPreparedDbContext("Cleanup database"))
        {
            operation.JellyfinDbContext.AttachmentStreamInfos.ExecuteDelete();
            operation.JellyfinDbContext.BaseItems.ExecuteDelete();
            operation.JellyfinDbContext.ItemValues.ExecuteDelete();
            operation.JellyfinDbContext.UserData.ExecuteDelete();
            operation.JellyfinDbContext.MediaStreamInfos.ExecuteDelete();
            operation.JellyfinDbContext.Peoples.ExecuteDelete();
            operation.JellyfinDbContext.PeopleBaseItemMap.ExecuteDelete();
            operation.JellyfinDbContext.Chapters.ExecuteDelete();
            operation.JellyfinDbContext.AncestorIds.ExecuteDelete();
        }

        // notify the other migration to just silently abort because the fix has been applied here already.
        ReseedFolderFlag.RerunGuardFlag = true;

        Dictionary<string, BaseItemEntity> legacyBaseItemWithUserKeys = new Dictionary<string, BaseItemEntity>();
        connection.Open();

        HashSet<Guid> baseItemIds = new HashSet<Guid>();
        using (DatabaseMigrationStep operation = GetPreparedDbContext("Moving TypedBaseItem"))
        {
            IDictionary<Guid, (BaseItemEntity BaseItem, string[] Keys)> allItemsLookup = new Dictionary<Guid, (BaseItemEntity BaseItem, string[] Keys)>();
            const string typedBaseItemsQuery =
            """
            SELECT guid, type, data, StartDate, EndDate, ChannelId, IsMovie,
            IsSeries, EpisodeTitle, IsRepeat, CommunityRating, CustomRating, IndexNumber, IsLocked, PreferredMetadataLanguage,
            PreferredMetadataCountryCode, Width, Height, DateLastRefreshed, Name, Path, PremiereDate, Overview, ParentIndexNumber,
            ProductionYear, OfficialRating, ForcedSortName, RunTimeTicks, Size, DateCreated, DateModified, Genres, ParentId, TopParentId,
            Audio, ExternalServiceId, IsInMixedFolder, DateLastSaved, LockedFields, Studios, Tags, TrailerTypes, OriginalTitle, PrimaryVersionId,
            DateLastMediaAdded, Album, LUFS, NormalizationGain, CriticRating, IsVirtualItem, SeriesName, UserDataKey, SeasonName, SeasonId, SeriesId,
            PresentationUniqueKey, InheritedParentalRatingValue, ExternalSeriesId, Tagline, ProviderIds, Images, ProductionLocations, ExtraIds, TotalBitrate,
            ExtraType, Artists, AlbumArtists, ExternalId, SeriesPresentationUniqueKey, ShowId, OwnerId, MediaType, SortName, CleanName, UnratedType, IsFolder FROM TypedBaseItems
            """;
            using (new TrackedMigrationStep("Loading TypedBaseItems", _logger))
            {
                foreach (SqliteDataReader dto in connection.Query(typedBaseItemsQuery))
                {
                    (BaseItemEntity BaseItem, string[] LegacyUserDataKey) baseItem = GetItem(dto);
                    allItemsLookup.Add(baseItem.BaseItem.Id, baseItem);
                }
            }

            bool DoesResolve(Guid? parentId, HashSet<(BaseItemEntity BaseItem, string[] Keys)> checkStack)
            {
                if (parentId is null)
                {
                    return true;
                }

                if (!allItemsLookup.TryGetValue(parentId.Value, out (BaseItemEntity BaseItem, string[] Keys) parent))
                {
                    return false; // item is detached and has no root anymore.
                }

                if (!checkStack.Add(parent))
                {
                    return false; // recursive structure. Abort.
                }

                return DoesResolve(parent.BaseItem.ParentId, checkStack);
            }

            using (new TrackedMigrationStep("Clean TypedBaseItems hierarchy", _logger))
            {
                HashSet<(BaseItemEntity BaseItem, string[] Keys)> checkStack = new HashSet<(BaseItemEntity BaseItem, string[] Keys)>();

                foreach (KeyValuePair<Guid, (BaseItemEntity BaseItem, string[] Keys)> item in allItemsLookup)
                {
                    (BaseItemEntity BaseItem, string[] Keys) cachedItem = item.Value;
                    if (DoesResolve(cachedItem.BaseItem.ParentId, checkStack))
                    {
                        checkStack.Add(cachedItem);
                        operation.JellyfinDbContext.BaseItems.Add(cachedItem.BaseItem);
                        baseItemIds.Add(cachedItem.BaseItem.Id);
                        foreach (string dataKey in cachedItem.Keys)
                        {
                            legacyBaseItemWithUserKeys[dataKey] = cachedItem.BaseItem;
                        }
                    }

                    checkStack.Clear();
                }
            }

            using (new TrackedMigrationStep($"Saving {operation.JellyfinDbContext.BaseItems.Local.Count} BaseItem entries", _logger))
            {
                operation.JellyfinDbContext.SaveChanges();
            }

            allItemsLookup.Clear();
        }

        using (DatabaseMigrationStep operation = GetPreparedDbContext("Moving ItemValues"))
        {
            // do not migrate inherited types as they are now properly mapped in search and lookup.
            const string itemValueQuery =
            """
            SELECT ItemId, Type, Value, CleanValue FROM ItemValues
                        WHERE Type <> 6 AND EXISTS(SELECT 1 FROM TypedBaseItems WHERE TypedBaseItems.guid = ItemValues.ItemId)
            """;

            // EFCores local lookup sucks. We cannot use context.ItemValues.Local here because its just super slow.
            Dictionary<(int Type, string Value), (ItemValue ItemValue, List<Guid> ItemIds)> localItems = new Dictionary<(int Type, string Value), (Database.Implementations.Entities.ItemValue ItemValue, List<Guid> ItemIds)>();
            using (new TrackedMigrationStep("Loading ItemValues", _logger))
            {
                foreach (SqliteDataReader dto in connection.Query(itemValueQuery))
                {
                    Guid itemId = dto.GetGuid(0);
                    if (!baseItemIds.Contains(itemId))
                    {
                        continue;
                    }

                    ItemValue entity = GetItemValue(dto);
                    (int, string Value) key = ((int)entity.Type, entity.Value);
                    if (!localItems.TryGetValue(key, out (ItemValue ItemValue, List<Guid> ItemIds) existing))
                    {
                        localItems[key] = existing = (entity, []);
                    }

                    existing.ItemIds.Add(itemId);
                }

                foreach (KeyValuePair<(int Type, string Value), (ItemValue ItemValue, List<Guid> ItemIds)> item in localItems)
                {
                    operation.JellyfinDbContext.ItemValues.Add(item.Value.ItemValue);
                    operation.JellyfinDbContext.ItemValuesMap.AddRange(item.Value.ItemIds.Distinct().Select(f => new ItemValueMap()
                    {
                        Item = null!,
                        ItemValue = null!,
                        ItemId = f,
                        ItemValueId = item.Value.ItemValue.ItemValueId
                    }));
                }
            }

            using (new TrackedMigrationStep($"Saving {operation.JellyfinDbContext.ItemValues.Local.Count} ItemValues entries", _logger))
            {
                operation.JellyfinDbContext.SaveChanges();
            }
        }

        using (DatabaseMigrationStep operation = GetPreparedDbContext("Moving UserData"))
        {
            IEnumerable<SqliteDataReader> queryResult = connection.Query(
            """
            SELECT key, userId, rating, played, playCount, isFavorite, playbackPositionTicks, lastPlayedDate, AudioStreamIndex, SubtitleStreamIndex FROM UserDatas

            WHERE EXISTS(SELECT 1 FROM TypedBaseItems WHERE TypedBaseItems.UserDataKey = UserDatas.key)
            """);

            using (new TrackedMigrationStep("Loading UserData", _logger))
            {
                User[] users = operation.JellyfinDbContext.Users.AsNoTracking().ToArray();
                HashSet<int> userIdBlacklist = new HashSet<int>();

                foreach (SqliteDataReader entity in queryResult)
                {
                    UserData? userData = GetUserData(users, entity, userIdBlacklist, _logger);
                    if (userData is null)
                    {
                        string userDataId = entity.GetString(0);
                        int internalUserId = entity.GetInt32(1);

                        if (!userIdBlacklist.Contains(internalUserId))
                        {
                            _logger.LogError("Was not able to migrate user data with key {0} because its id {InternalId} does not match any existing user.", userDataId, internalUserId);
                            userIdBlacklist.Add(internalUserId);
                        }

                        continue;
                    }

                    if (!legacyBaseItemWithUserKeys.TryGetValue(userData.CustomDataKey!, out BaseItemEntity? refItem))
                    {
                        _logger.LogError("Was not able to migrate user data with key {0} because it does not reference a valid BaseItem.", entity.GetString(0));
                        continue;
                    }

                    if (!baseItemIds.Contains(refItem.Id))
                    {
                        continue;
                    }

                    userData.ItemId = refItem.Id;
                    operation.JellyfinDbContext.UserData.Add(userData);
                }
            }

            legacyBaseItemWithUserKeys.Clear();

            using (new TrackedMigrationStep($"Saving {operation.JellyfinDbContext.UserData.Local.Count} UserData entries", _logger))
            {
                operation.JellyfinDbContext.SaveChanges();
            }
        }

        using (DatabaseMigrationStep operation = GetPreparedDbContext("Moving MediaStreamInfos"))
        {
            const string mediaStreamQuery =
            """
            SELECT ItemId, StreamIndex, StreamType, Codec, Language, ChannelLayout, Profile, AspectRatio, Path,
            IsInterlaced, BitRate, Channels, SampleRate, IsDefault, IsForced, IsExternal, Height, Width,
            AverageFrameRate, RealFrameRate, Level, PixelFormat, BitDepth, IsAnamorphic, RefFrames, CodecTag,
            Comment, NalLengthSize, IsAvc, Title, TimeBase, CodecTimeBase, ColorPrimaries, ColorSpace, ColorTransfer,
            DvVersionMajor, DvVersionMinor, DvProfile, DvLevel, RpuPresentFlag, ElPresentFlag, BlPresentFlag, DvBlSignalCompatibilityId, IsHearingImpaired
            FROM MediaStreams
            WHERE EXISTS(SELECT 1 FROM TypedBaseItems WHERE TypedBaseItems.guid = MediaStreams.ItemId)
            """;

            using (new TrackedMigrationStep("Loading MediaStreamInfos", _logger))
            {
                foreach (SqliteDataReader dto in connection.Query(mediaStreamQuery))
                {
                    MediaStreamInfo entity = GetMediaStream(dto);
                    if (!baseItemIds.Contains(entity.ItemId))
                    {
                        continue;
                    }

                    operation.JellyfinDbContext.MediaStreamInfos.Add(entity);
                }
            }

            using (new TrackedMigrationStep($"Saving {operation.JellyfinDbContext.MediaStreamInfos.Local.Count} MediaStreamInfos entries", _logger))
            {
                operation.JellyfinDbContext.SaveChanges();
            }
        }

        using (DatabaseMigrationStep operation = GetPreparedDbContext("Moving AttachmentStreamInfos"))
        {
            const string mediaAttachmentQuery =
            """
            SELECT ItemId, AttachmentIndex, Codec, CodecTag, Comment, filename, MIMEType
            FROM mediaattachments
            WHERE EXISTS(SELECT 1 FROM TypedBaseItems WHERE TypedBaseItems.guid = mediaattachments.ItemId)
            """;

            using (new TrackedMigrationStep("Loading AttachmentStreamInfos", _logger))
            {
                foreach (SqliteDataReader dto in connection.Query(mediaAttachmentQuery))
                {
                    AttachmentStreamInfo entity = GetMediaAttachment(dto);
                    if (!baseItemIds.Contains(entity.ItemId))
                    {
                        continue;
                    }

                    operation.JellyfinDbContext.AttachmentStreamInfos.Add(entity);
                }
            }

            using (new TrackedMigrationStep($"Saving {operation.JellyfinDbContext.AttachmentStreamInfos.Local.Count} AttachmentStreamInfos entries", _logger))
            {
                operation.JellyfinDbContext.SaveChanges();
            }
        }

        using (DatabaseMigrationStep operation = GetPreparedDbContext("Moving People"))
        {
            const string personsQuery =
            """
            SELECT ItemId, Name, Role, PersonType, SortOrder, ListOrder FROM People
            WHERE EXISTS(SELECT 1 FROM TypedBaseItems WHERE TypedBaseItems.guid = People.ItemId)
            """;

            Dictionary<string, (People Person, List<PeopleBaseItemMap> Items)> peopleCache = new Dictionary<string, (People Person, List<PeopleBaseItemMap> Items)>();

            using (new TrackedMigrationStep("Loading People", _logger))
            {
                foreach (SqliteDataReader reader in connection.Query(personsQuery))
                {
                    Guid itemId = reader.GetGuid(0);
                    if (!baseItemIds.Contains(itemId))
                    {
                        _logger.LogError("Not saving person {0} because it's not in use by any BaseItem", reader.GetString(1));
                        continue;
                    }

                    People entity = GetPerson(reader);
                    if (!peopleCache.TryGetValue(entity.Name + "|" + entity.PersonType, out (People Person, List<PeopleBaseItemMap> Items) personCache))
                    {
                        peopleCache[entity.Name + "|" + entity.PersonType] = personCache = (entity, []);
                    }

                    if (reader.TryGetString(2, out string? role))
                    {
                    }

                    int? sortOrder = reader.IsDBNull(4) ? null : reader.GetInt32(4);
                    int? listOrder = reader.IsDBNull(5) ? null : reader.GetInt32(5);

                    personCache.Items.Add(new PeopleBaseItemMap()
                    {
                        Item = null!,
                        ItemId = itemId,
                        People = null!,
                        PeopleId = personCache.Person.Id,
                        ListOrder = listOrder,
                        SortOrder = sortOrder,
                        Role = role
                    });
                }

                foreach (KeyValuePair<string, (People Person, List<PeopleBaseItemMap> Items)> item in peopleCache)
                {
                    operation.JellyfinDbContext.Peoples.Add(item.Value.Person);
                    operation.JellyfinDbContext.PeopleBaseItemMap.AddRange(item.Value.Items.DistinctBy(e => (e.ItemId, e.PeopleId)));
                }

                peopleCache.Clear();
            }

            using (new TrackedMigrationStep($"Saving {operation.JellyfinDbContext.Peoples.Local.Count} People entries and {operation.JellyfinDbContext.PeopleBaseItemMap.Local.Count} maps", _logger))
            {
                operation.JellyfinDbContext.SaveChanges();
            }
        }

        using (DatabaseMigrationStep operation = GetPreparedDbContext("Moving Chapters"))
        {
            const string chapterQuery =
            """
            SELECT ItemId,StartPositionTicks,Name,ImagePath,ImageDateModified,ChapterIndex from Chapters2
            WHERE EXISTS(SELECT 1 FROM TypedBaseItems WHERE TypedBaseItems.guid = Chapters2.ItemId)
            """;

            using (new TrackedMigrationStep("Loading Chapters", _logger))
            {
                foreach (SqliteDataReader dto in connection.Query(chapterQuery))
                {
                    Chapter chapter = GetChapter(dto);
                    if (!baseItemIds.Contains(chapter.ItemId))
                    {
                        continue;
                    }

                    operation.JellyfinDbContext.Chapters.Add(chapter);
                }
            }

            using (new TrackedMigrationStep($"Saving {operation.JellyfinDbContext.Chapters.Local.Count} Chapters entries", _logger))
            {
                operation.JellyfinDbContext.SaveChanges();
            }
        }

        using (DatabaseMigrationStep operation = GetPreparedDbContext("Moving AncestorIds"))
        {
            const string ancestorIdsQuery =
            """
            SELECT ItemId, AncestorId, AncestorIdText FROM AncestorIds
            WHERE
            EXISTS(SELECT 1 FROM TypedBaseItems WHERE TypedBaseItems.guid = AncestorIds.ItemId)
            AND
            EXISTS(SELECT 1 FROM TypedBaseItems WHERE TypedBaseItems.guid = AncestorIds.AncestorId)
            """;

            using (new TrackedMigrationStep("Loading AncestorIds", _logger))
            {
                foreach (SqliteDataReader dto in connection.Query(ancestorIdsQuery))
                {
                    AncestorId ancestorId = GetAncestorId(dto);
                    if (!baseItemIds.Contains(ancestorId.ItemId) || !baseItemIds.Contains(ancestorId.ParentItemId))
                    {
                        continue;
                    }

                    operation.JellyfinDbContext.AncestorIds.Add(ancestorId);
                }
            }

            using (new TrackedMigrationStep($"Saving {operation.JellyfinDbContext.AncestorIds.Local.Count} AncestorId entries", _logger))
            {
                operation.JellyfinDbContext.SaveChanges();
            }
        }

        connection.Close();

        _logger.LogInformation("Migration of the Library.db done.");
        _logger.LogInformation("Migrating Library db took {0}.", fullOperationTimer.Elapsed);

        SqliteConnection.ClearAllPools();

        using (SqliteConnection checkpointConnection = new SqliteConnection($"Filename={libraryDbPath}"))
        {
            checkpointConnection.Open();
            using SqliteCommand cmd = checkpointConnection.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        _logger.LogInformation("Move {0} to {1}.", libraryDbPath, libraryDbPath + ".old");
        File.Move(libraryDbPath, libraryDbPath + ".old", true);
    }

    private DatabaseMigrationStep GetPreparedDbContext(string operationName)
    {
        JellyfinDbContext dbContext = _provider.CreateDbContext();
        dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
        dbContext.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return new DatabaseMigrationStep(dbContext, operationName, _logger);
    }

    internal static UserData? GetUserData(User[] users, SqliteDataReader dto, HashSet<int> userIdBlacklist, ILogger logger)
    {
        int internalUserId = dto.GetInt32(1);
        if (userIdBlacklist.Contains(internalUserId))
        {
            return null;
        }

        User? user = users.FirstOrDefault(e => e.InternalId == internalUserId);
        if (user is null)
        {
            userIdBlacklist.Add(internalUserId);

            logger.LogError("Tried to find user with index '{Idx}' but there are only '{MaxIdx}' users.", internalUserId, users.Length);
            return null;
        }

        string oldKey = dto.GetString(0);

        return new UserData()
        {
            ItemId = Guid.NewGuid(),
            CustomDataKey = oldKey,
            UserId = user.Id,
            Rating = dto.IsDBNull(2) ? null : dto.GetDouble(2),
            Played = dto.GetBoolean(3),
            PlayCount = dto.GetInt32(4),
            IsFavorite = dto.GetBoolean(5),
            PlaybackPositionTicks = dto.GetInt64(6),
            LastPlayedDate = dto.IsDBNull(7) ? null : dto.GetDateTime(7),
            AudioStreamIndex = dto.IsDBNull(8) ? null : dto.GetInt32(8),
            SubtitleStreamIndex = dto.IsDBNull(9) ? null : dto.GetInt32(9),
            Likes = null,
            User = null!,
            Item = null!
        };
    }

    private AncestorId GetAncestorId(SqliteDataReader reader)
    {
        return new AncestorId()
        {
            ItemId = reader.GetGuid(0),
            ParentItemId = reader.GetGuid(1),
            Item = null!,
            ParentItem = null!
        };
    }

    /// <summary>
    /// Gets the chapter.
    /// </summary>
    /// <param name="reader">The reader.</param>
    /// <returns>ChapterInfo.</returns>
    private Chapter GetChapter(SqliteDataReader reader)
    {
        Chapter chapter = new Chapter
        {
            StartPositionTicks = reader.GetInt64(1),
            ChapterIndex = reader.GetInt32(5),
            Item = null!,
            ItemId = reader.GetGuid(0),
        };

        if (reader.TryGetString(2, out string? chapterName))
        {
            chapter.Name = chapterName;
        }

        if (reader.TryGetString(3, out string? imagePath))
        {
            chapter.ImagePath = imagePath;
        }

        if (reader.TryReadDateTime(4, out DateTime imageDateModified))
        {
            chapter.ImageDateModified = imageDateModified;
        }

        return chapter;
    }

    private ItemValue GetItemValue(SqliteDataReader reader)
    {
        return new ItemValue
        {
            ItemValueId = Guid.NewGuid(),
            Type = (ItemValueType)reader.GetInt32(1),
            Value = reader.GetString(2),
            CleanValue = reader.GetString(3),
        };
    }

    private People GetPerson(SqliteDataReader reader)
    {
        People item = new People
        {
            Id = Guid.NewGuid(),
            Name = reader.GetString(1),
        };

        if (reader.TryGetString(3, out string? type))
        {
            item.PersonType = type;
        }

        return item;
    }

    /// <summary>
    /// Gets the media stream.
    /// </summary>
    /// <param name="reader">The reader.</param>
    /// <returns>MediaStream.</returns>
    private MediaStreamInfo GetMediaStream(SqliteDataReader reader)
    {
        MediaStreamInfo item = new MediaStreamInfo
        {
            StreamIndex = reader.GetInt32(1),
            StreamType = Enum.Parse<MediaStreamTypeEntity>(reader.GetString(2)),
            Item = null!,
            ItemId = reader.GetGuid(0),
            AspectRatio = null!,
            ChannelLayout = null!,
            Codec = null!,
            IsInterlaced = false,
            Language = null!,
            Path = null!,
            Profile = null!,
        };

        if (reader.TryGetString(3, out string? codec))
        {
            item.Codec = codec;
        }

        if (reader.TryGetString(4, out string? language))
        {
            item.Language = language;
        }

        if (reader.TryGetString(5, out string? channelLayout))
        {
            item.ChannelLayout = channelLayout;
        }

        if (reader.TryGetString(6, out string? profile))
        {
            item.Profile = profile;
        }

        if (reader.TryGetString(7, out string? aspectRatio))
        {
            item.AspectRatio = aspectRatio;
        }

        if (reader.TryGetString(8, out string? path))
        {
            item.Path = path;
        }

        item.IsInterlaced = reader.GetBoolean(9);

        if (reader.TryGetInt32(10, out int bitrate))
        {
            item.BitRate = bitrate;
        }

        if (reader.TryGetInt32(11, out int channels))
        {
            item.Channels = channels;
        }

        if (reader.TryGetInt32(12, out int sampleRate))
        {
            item.SampleRate = sampleRate;
        }

        item.IsDefault = reader.GetBoolean(13);
        item.IsForced = reader.GetBoolean(14);
        item.IsExternal = reader.GetBoolean(15);

        if (reader.TryGetInt32(16, out int width))
        {
            item.Width = width;
        }

        if (reader.TryGetInt32(17, out int height))
        {
            item.Height = height;
        }

        if (reader.TryGetSingle(18, out float averageFrameRate))
        {
            item.AverageFrameRate = averageFrameRate;
        }

        if (reader.TryGetSingle(19, out float realFrameRate))
        {
            item.RealFrameRate = realFrameRate;
        }

        if (reader.TryGetSingle(20, out float level))
        {
            item.Level = level;
        }

        if (reader.TryGetString(21, out string? pixelFormat))
        {
            item.PixelFormat = pixelFormat;
        }

        if (reader.TryGetInt32(22, out int bitDepth))
        {
            item.BitDepth = bitDepth;
        }

        if (reader.TryGetBoolean(23, out bool isAnamorphic))
        {
            item.IsAnamorphic = isAnamorphic;
        }

        if (reader.TryGetInt32(24, out int refFrames))
        {
            item.RefFrames = refFrames;
        }

        if (reader.TryGetString(25, out string? codecTag))
        {
            item.CodecTag = codecTag;
        }

        if (reader.TryGetString(26, out string? comment))
        {
            item.Comment = comment;
        }

        if (reader.TryGetString(27, out string? nalLengthSize))
        {
            item.NalLengthSize = nalLengthSize;
        }

        if (reader.TryGetBoolean(28, out bool isAVC))
        {
            item.IsAvc = isAVC;
        }

        if (reader.TryGetString(29, out string? title))
        {
            item.Title = title;
        }

        if (reader.TryGetString(30, out string? timeBase))
        {
            item.TimeBase = timeBase;
        }

        if (reader.TryGetString(31, out string? codecTimeBase))
        {
            item.CodecTimeBase = codecTimeBase;
        }

        if (reader.TryGetString(32, out string? colorPrimaries))
        {
            item.ColorPrimaries = colorPrimaries;
        }

        if (reader.TryGetString(33, out string? colorSpace))
        {
            item.ColorSpace = colorSpace;
        }

        if (reader.TryGetString(34, out string? colorTransfer))
        {
            item.ColorTransfer = colorTransfer;
        }

        if (reader.TryGetInt32(35, out int dvVersionMajor))
        {
            item.DvVersionMajor = dvVersionMajor;
        }

        if (reader.TryGetInt32(36, out int dvVersionMinor))
        {
            item.DvVersionMinor = dvVersionMinor;
        }

        if (reader.TryGetInt32(37, out int dvProfile))
        {
            item.DvProfile = dvProfile;
        }

        if (reader.TryGetInt32(38, out int dvLevel))
        {
            item.DvLevel = dvLevel;
        }

        if (reader.TryGetInt32(39, out int rpuPresentFlag))
        {
            item.RpuPresentFlag = rpuPresentFlag;
        }

        if (reader.TryGetInt32(40, out int elPresentFlag))
        {
            item.ElPresentFlag = elPresentFlag;
        }

        if (reader.TryGetInt32(41, out int blPresentFlag))
        {
            item.BlPresentFlag = blPresentFlag;
        }

        if (reader.TryGetInt32(42, out int dvBlSignalCompatibilityId))
        {
            item.DvBlSignalCompatibilityId = dvBlSignalCompatibilityId;
        }

        item.IsHearingImpaired = reader.TryGetBoolean(43, out bool result) && result;

        // if (reader.TryGetInt32(44, out var rotation))
        // {
        //     item.Rotation = rotation;
        // }

        return item;
    }

    /// <summary>
    /// Gets the attachment.
    /// </summary>
    /// <param name="reader">The reader.</param>
    /// <returns>MediaAttachment.</returns>
    private AttachmentStreamInfo GetMediaAttachment(SqliteDataReader reader)
    {
        AttachmentStreamInfo item = new AttachmentStreamInfo
        {
            Index = reader.GetInt32(1),
            Item = null!,
            ItemId = reader.GetGuid(0),
        };

        if (reader.TryGetString(2, out string? codec))
        {
            item.Codec = codec;
        }

        if (reader.TryGetString(3, out string? codecTag))
        {
            item.CodecTag = codecTag;
        }

        if (reader.TryGetString(4, out string? comment))
        {
            item.Comment = comment;
        }

        if (reader.TryGetString(5, out string? fileName))
        {
            item.Filename = fileName;
        }

        if (reader.TryGetString(6, out string? mimeType))
        {
            item.MimeType = mimeType;
        }

        return item;
    }

    private (BaseItemEntity BaseItem, string[] LegacyUserDataKey) GetItem(SqliteDataReader reader)
    {
        BaseItemEntity entity = new BaseItemEntity()
        {
            Id = reader.GetGuid(0),
            Type = reader.GetString(1),
        };

        int index = 2;

        if (reader.TryGetString(index++, out string? data))
        {
            entity.Data = data;
        }

        if (reader.TryReadDateTime(index++, out DateTime startDate))
        {
            entity.StartDate = startDate;
        }

        if (reader.TryReadDateTime(index++, out DateTime endDate))
        {
            entity.EndDate = endDate;
        }

        if (reader.TryGetGuid(index++, out Guid guid))
        {
            entity.ChannelId = guid;
        }

        if (reader.TryGetBoolean(index++, out bool isMovie))
        {
            entity.IsMovie = isMovie;
        }

        if (reader.TryGetBoolean(index++, out bool isSeries))
        {
            entity.IsSeries = isSeries;
        }

        if (reader.TryGetString(index++, out string? episodeTitle))
        {
            entity.EpisodeTitle = episodeTitle;
        }

        if (reader.TryGetBoolean(index++, out bool isRepeat))
        {
            entity.IsRepeat = isRepeat;
        }

        if (reader.TryGetSingle(index++, out float communityRating))
        {
            entity.CommunityRating = communityRating;
        }

        if (reader.TryGetString(index++, out string? customRating))
        {
            entity.CustomRating = customRating;
        }

        if (reader.TryGetInt32(index++, out int indexNumber))
        {
            entity.IndexNumber = indexNumber;
        }

        if (reader.TryGetBoolean(index++, out bool isLocked))
        {
            entity.IsLocked = isLocked;
        }

        if (reader.TryGetString(index++, out string? preferredMetadataLanguage))
        {
            entity.PreferredMetadataLanguage = preferredMetadataLanguage;
        }

        if (reader.TryGetString(index++, out string? preferredMetadataCountryCode))
        {
            entity.PreferredMetadataCountryCode = preferredMetadataCountryCode;
        }

        if (reader.TryGetInt32(index++, out int width))
        {
            entity.Width = width;
        }

        if (reader.TryGetInt32(index++, out int height))
        {
            entity.Height = height;
        }

        if (reader.TryReadDateTime(index++, out DateTime dateLastRefreshed))
        {
            entity.DateLastRefreshed = dateLastRefreshed;
        }

        if (reader.TryGetString(index++, out string? name))
        {
            entity.Name = name;
        }

        if (reader.TryGetString(index++, out string? restorePath))
        {
            entity.Path = restorePath;
        }

        if (reader.TryReadDateTime(index++, out DateTime premiereDate))
        {
            entity.PremiereDate = premiereDate;
        }

        if (reader.TryGetString(index++, out string? overview))
        {
            entity.Overview = overview;
        }

        if (reader.TryGetInt32(index++, out int parentIndexNumber))
        {
            entity.ParentIndexNumber = parentIndexNumber;
        }

        if (reader.TryGetInt32(index++, out int productionYear))
        {
            entity.ProductionYear = productionYear;
        }

        if (reader.TryGetString(index++, out string? officialRating))
        {
            entity.OfficialRating = officialRating;
        }

        if (reader.TryGetString(index++, out string? forcedSortName))
        {
            entity.ForcedSortName = forcedSortName;
        }

        if (reader.TryGetInt64(index++, out long runTimeTicks))
        {
            entity.RunTimeTicks = runTimeTicks;
        }

        if (reader.TryGetInt64(index++, out long size))
        {
            entity.Size = size;
        }

        if (reader.TryReadDateTime(index++, out DateTime dateCreated))
        {
            entity.DateCreated = dateCreated;
        }

        if (reader.TryReadDateTime(index++, out DateTime dateModified))
        {
            entity.DateModified = dateModified;
        }

        if (reader.TryGetString(index++, out string? genres))
        {
            entity.Genres = genres;
        }

        if (reader.TryGetGuid(index++, out Guid parentId))
        {
            entity.ParentId = parentId;
        }

        if (reader.TryGetGuid(index++, out Guid topParentId))
        {
            entity.TopParentId = topParentId;
        }

        if (reader.TryGetString(index++, out string? audioString) && Enum.TryParse<ProgramAudioEntity>(audioString, out ProgramAudioEntity audioType))
        {
            entity.Audio = audioType;
        }

        if (reader.TryGetString(index++, out string? serviceName))
        {
            entity.ExternalServiceId = serviceName;
        }

        if (reader.TryGetBoolean(index++, out bool isInMixedFolder))
        {
            entity.IsInMixedFolder = isInMixedFolder;
        }

        if (reader.TryReadDateTime(index++, out DateTime dateLastSaved))
        {
            entity.DateLastSaved = dateLastSaved;
        }

        if (reader.TryGetString(index++, out string? lockedFields))
        {
            entity.LockedFields = lockedFields.Split('|').Select(Enum.Parse<MetadataField>)
                .Select(e => new BaseItemMetadataField()
                {
                    Id = (int)e,
                    Item = entity,
                    ItemId = entity.Id
                })
                .ToArray();
        }

        if (reader.TryGetString(index++, out string? studios))
        {
            entity.Studios = studios;
        }

        if (reader.TryGetString(index++, out string? tags))
        {
            entity.Tags = tags;
        }

        if (reader.TryGetString(index++, out string? trailerTypes))
        {
            entity.TrailerTypes = trailerTypes.Split('|').Select(Enum.Parse<TrailerType>)
                .Select(e => new BaseItemTrailerType()
                {
                    Id = (int)e,
                    Item = entity,
                    ItemId = entity.Id
                })
                .ToArray();
        }

        if (reader.TryGetString(index++, out string? originalTitle))
        {
            entity.OriginalTitle = originalTitle;
        }

        if (reader.TryGetString(index++, out string? primaryVersionId))
        {
            entity.PrimaryVersionId = primaryVersionId;
        }

        if (reader.TryReadDateTime(index++, out DateTime dateLastMediaAdded))
        {
            entity.DateLastMediaAdded = dateLastMediaAdded;
        }

        if (reader.TryGetString(index++, out string? album))
        {
            entity.Album = album;
        }

        if (reader.TryGetSingle(index++, out float lUFS))
        {
            entity.LUFS = lUFS;
        }

        if (reader.TryGetSingle(index++, out float normalizationGain))
        {
            entity.NormalizationGain = normalizationGain;
        }

        if (reader.TryGetSingle(index++, out float criticRating))
        {
            entity.CriticRating = criticRating;
        }

        if (reader.TryGetBoolean(index++, out bool isVirtualItem))
        {
            entity.IsVirtualItem = isVirtualItem;
        }

        if (reader.TryGetString(index++, out string? seriesName))
        {
            entity.SeriesName = seriesName;
        }

        List<string> userDataKeys = new List<string>();
        if (reader.TryGetString(index++, out string? directUserDataKey))
        {
            userDataKeys.Add(directUserDataKey);
        }

        if (reader.TryGetString(index++, out string? seasonName))
        {
            entity.SeasonName = seasonName;
        }

        if (reader.TryGetGuid(index++, out Guid seasonId))
        {
            entity.SeasonId = seasonId;
        }

        if (reader.TryGetGuid(index++, out Guid seriesId))
        {
            entity.SeriesId = seriesId;
        }

        if (reader.TryGetString(index++, out string? presentationUniqueKey))
        {
            entity.PresentationUniqueKey = presentationUniqueKey;
        }

        if (reader.TryGetInt32(index++, out int parentalRating))
        {
            entity.InheritedParentalRatingValue = parentalRating;
        }

        if (reader.TryGetString(index++, out string? externalSeriesId))
        {
            entity.ExternalSeriesId = externalSeriesId;
        }

        if (reader.TryGetString(index++, out string? tagLine))
        {
            entity.Tagline = tagLine;
        }

        if (reader.TryGetString(index++, out string? providerIds))
        {
            entity.Provider = providerIds.Split('|').Select(e => e.Split("=")).Where(e => e.Length >= 2)
            .Select(e => new BaseItemProvider()
            {
                Item = null!,
                ProviderId = e[0],
                ProviderValue = string.Join('|', e.Skip(1))
            })
            .DistinctBy(e => e.ProviderId)
            .ToArray();
        }

        if (reader.TryGetString(index++, out string? imageInfos))
        {
            entity.Images = DeserializeImages(imageInfos).Select(f => Map(entity.Id, f)).ToArray();
        }

        if (reader.TryGetString(index++, out string? productionLocations))
        {
            entity.ProductionLocations = productionLocations;
        }

        if (reader.TryGetString(index++, out string? extraIds))
        {
            entity.ExtraIds = extraIds;
        }

        if (reader.TryGetInt32(index++, out int totalBitrate))
        {
            entity.TotalBitrate = totalBitrate;
        }

        if (reader.TryGetString(index++, out string? extraTypeString) && Enum.TryParse<BaseItemExtraType>(extraTypeString, out BaseItemExtraType extraType))
        {
            entity.ExtraType = extraType;
        }

        if (reader.TryGetString(index++, out string? artists))
        {
            entity.Artists = artists;
        }

        if (reader.TryGetString(index++, out string? albumArtists))
        {
            entity.AlbumArtists = albumArtists;
        }

        if (reader.TryGetString(index++, out string? externalId))
        {
            entity.ExternalId = externalId;
        }

        if (reader.TryGetString(index++, out string? seriesPresentationUniqueKey))
        {
            entity.SeriesPresentationUniqueKey = seriesPresentationUniqueKey;
        }

        if (reader.TryGetString(index++, out string? showId))
        {
            entity.ShowId = showId;
        }

        if (reader.TryGetString(index++, out string? ownerId))
        {
            entity.OwnerId = ownerId;
        }

        if (reader.TryGetString(index++, out string? mediaType))
        {
            entity.MediaType = mediaType;
        }

        if (reader.TryGetString(index++, out string? sortName))
        {
            entity.SortName = sortName;
        }

        if (reader.TryGetString(index++, out string? cleanName))
        {
            entity.CleanName = cleanName;
        }

        if (reader.TryGetString(index++, out string? unratedType))
        {
            entity.UnratedType = unratedType;
        }

        if (reader.TryGetBoolean(index++, out bool isFolder))
        {
            entity.IsFolder = isFolder;
        }

        BaseItem baseItem = BaseItemRepository.DeserializeBaseItem(entity, _logger, null, false);
        List<string> dataKeys = baseItem.GetUserDataKeys();
        userDataKeys.AddRange(dataKeys);

        return (entity, userDataKeys.ToArray());
    }

    private static BaseItemImageInfo Map(Guid baseItemId, ItemImageInfo e)
    {
        return new BaseItemImageInfo()
        {
            ItemId = baseItemId,
            Id = Guid.NewGuid(),
            Path = e.Path,
            Blurhash = e.BlurHash is not null ? Encoding.UTF8.GetBytes(e.BlurHash) : null,
            DateModified = e.DateModified,
            Height = e.Height,
            Width = e.Width,
            ImageType = (ImageInfoImageType)e.Type,
            Item = null!
        };
    }

    internal ItemImageInfo[] DeserializeImages(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<ItemImageInfo>();
        }

        // TODO The following is an ugly performance optimization, but it's extremely unlikely that the data in the database would be malformed
        ReadOnlySpan<char> valueSpan = value.AsSpan();
        int count = valueSpan.Count('|') + 1;

        int position = 0;
        ItemImageInfo[] result = new ItemImageInfo[count];
        foreach (ReadOnlySpan<char> part in valueSpan.Split('|'))
        {
            ItemImageInfo? image = ItemImageInfoFromValueString(part);

            if (image is not null)
            {
                result[position++] = image;
            }
        }

        if (position == count)
        {
            return result;
        }

        if (position == 0)
        {
            return Array.Empty<ItemImageInfo>();
        }

        // Extremely unlikely, but somehow one or more of the image strings were malformed. Cut the array.
        return result[..position];
    }

    internal ItemImageInfo? ItemImageInfoFromValueString(ReadOnlySpan<char> value)
    {
        const char Delimiter = '*';

        int nextSegment = value.IndexOf(Delimiter);
        if (nextSegment == -1)
        {
            return null;
        }

        ReadOnlySpan<char> path = value[..nextSegment];
        value = value[(nextSegment + 1)..];
        nextSegment = value.IndexOf(Delimiter);
        if (nextSegment == -1)
        {
            return null;
        }

        ReadOnlySpan<char> dateModified = value[..nextSegment];
        value = value[(nextSegment + 1)..];
        nextSegment = value.IndexOf(Delimiter);
        if (nextSegment == -1)
        {
            nextSegment = value.Length;
        }

        ReadOnlySpan<char> imageType = value[..nextSegment];

        ItemImageInfo image = new ItemImageInfo
        {
            Path = path.ToString()
        };

        if (long.TryParse(dateModified, CultureInfo.InvariantCulture, out long ticks)
            && ticks >= DateTime.MinValue.Ticks
            && ticks <= DateTime.MaxValue.Ticks)
        {
            image.DateModified = new DateTime(ticks, DateTimeKind.Utc);
        }
        else
        {
            return null;
        }

        if (Enum.TryParse(imageType, true, out ImageType type))
        {
            image.Type = type;
        }
        else
        {
            return null;
        }

        // Optional parameters: width*height*blurhash
        if (nextSegment + 1 < value.Length - 1)
        {
            value = value[(nextSegment + 1)..];
            nextSegment = value.IndexOf(Delimiter);
            if (nextSegment == -1 || nextSegment == value.Length)
            {
                return image;
            }

            ReadOnlySpan<char> widthSpan = value[..nextSegment];

            value = value[(nextSegment + 1)..];
            nextSegment = value.IndexOf(Delimiter);
            if (nextSegment == -1)
            {
                nextSegment = value.Length;
            }

            ReadOnlySpan<char> heightSpan = value[..nextSegment];

            if (int.TryParse(widthSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
                && int.TryParse(heightSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int height))
            {
                image.Width = width;
                image.Height = height;
            }

            if (nextSegment < value.Length - 1)
            {
                value = value[(nextSegment + 1)..];
                int length = value.Length;

                Span<char> blurHashSpan = stackalloc char[length];
                for (int i = 0; i < length; i++)
                {
                    char c = value[i];
                    blurHashSpan[i] = c switch
                    {
                        '/' => Delimiter,
                        '\\' => '|',
                        _ => c
                    };
                }

                image.BlurHash = new string(blurHashSpan);
            }
        }

        return image;
    }

    private class TrackedMigrationStep : IDisposable
    {
        private readonly string _operationName;
        private readonly ILogger _logger;
        private readonly Stopwatch _operationTimer;
        private bool _disposed;

        public TrackedMigrationStep(string operationName, ILogger logger)
        {
            _operationName = operationName;
            _logger = logger;
            _operationTimer = Stopwatch.StartNew();
            logger.LogInformation("Start {OperationName}", operationName);
        }

        public bool Disposed
        {
            get => _disposed;
            set => _disposed = value;
        }

        public virtual void Dispose()
        {
            if (Disposed)
            {
                return;
            }

            Disposed = true;
            _logger.LogInformation("{OperationName} took '{Time}'", _operationName, _operationTimer.Elapsed);
        }
    }

    private sealed class DatabaseMigrationStep : TrackedMigrationStep
    {
        public DatabaseMigrationStep(JellyfinDbContext jellyfinDbContext, string operationName, ILogger logger) : base(operationName, logger)
        {
            JellyfinDbContext = jellyfinDbContext;
        }

        public JellyfinDbContext JellyfinDbContext { get; }

        public override void Dispose()
        {
            if (Disposed)
            {
                return;
            }

            JellyfinDbContext.Dispose();
            base.Dispose();
        }
    }
}
