using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.IO;

namespace Emby.Server.Implementations.Library;

/// <summary>
/// IPathManager implementation.
/// </summary>
public class PathManager : IPathManager
{
    private readonly IServerConfigurationManager _config;
    private readonly IApplicationPaths _appPaths;

    /// <summary>
    /// Initializes a new instance of the <see cref="PathManager"/> class.
    /// </summary>
    /// <param name="config">The server configuration manager.</param>
    /// <param name="appPaths">The application paths.</param>
    public PathManager(
        IServerConfigurationManager config,
        IApplicationPaths appPaths)
    {
        _config = config;
        _appPaths = appPaths;
    }

    private string SubtitleCachePath => Path.Combine(_appPaths.DataPath, "subtitles");

    private string AttachmentCachePath => Path.Combine(_appPaths.DataPath, "attachments");

    /// <inheritdoc />
    public string GetAttachmentPath(string mediaSourceId, string fileName)
    {
        return Path.Combine(GetAttachmentFolderPath(mediaSourceId), fileName);
    }

    /// <inheritdoc />
    public string GetAttachmentFolderPath(string mediaSourceId)
    {
        ReadOnlySpan<char> id = Guid.Parse(mediaSourceId).ToString("D", CultureInfo.InvariantCulture).AsSpan();

        return Path.Join(AttachmentCachePath, id[..2], id);
    }

    /// <inheritdoc />
    public string GetSubtitleFolderPath(string mediaSourceId)
    {
        ReadOnlySpan<char> id = Guid.Parse(mediaSourceId).ToString("D", CultureInfo.InvariantCulture).AsSpan();

        return Path.Join(SubtitleCachePath, id[..2], id);
    }

    /// <inheritdoc />
    public string GetSubtitlePath(string mediaSourceId, int streamIndex, string extension)
    {
        return Path.Combine(GetSubtitleFolderPath(mediaSourceId), streamIndex.ToString(CultureInfo.InvariantCulture) + extension);
    }

    /// <inheritdoc />
    public string GetTrickplayDirectory(BaseItem item, bool saveWithMedia = false)
    {
        ReadOnlySpan<char> id = item.Id.ToString("D", CultureInfo.InvariantCulture).AsSpan();

        return saveWithMedia
            ? Path.Combine(item.ContainingFolderPath, Path.ChangeExtension(Path.GetFileName(item.Path), ".trickplay"))
            : Path.Join(_config.ApplicationPaths.TrickplayPath, id[..2], id);
    }

    /// <inheritdoc/>
    public string GetChapterImageFolderPath(BaseItem item)
    {
        return Path.Combine(item.GetInternalMetadataPath(), "chapters");
    }

    /// <inheritdoc/>
    public string GetChapterImagePath(BaseItem item, long chapterPositionTicks)
    {
        string filename = item.DateModified.Ticks.ToString(CultureInfo.InvariantCulture) + "_" + chapterPositionTicks.ToString(CultureInfo.InvariantCulture) + ".jpg";

        return Path.Combine(GetChapterImageFolderPath(item), filename);
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetExtractedDataPaths(BaseItem item)
    {
        string mediaSourceId = item.Id.ToString("N", CultureInfo.InvariantCulture);
        return [
            GetAttachmentFolderPath(mediaSourceId),
            GetSubtitleFolderPath(mediaSourceId),
            GetTrickplayDirectory(item, false),
            GetTrickplayDirectory(item, true),
            GetChapterImageFolderPath(item)
        ];
    }
}
