using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Extensions;
using Jellyfin.Api.Helpers;
using Jellyfin.Api.Models.VisularrDtos;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Library;
using MediaBrowser.Model.MediaInfo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Api.Controllers;

/// <summary>
/// Subtitle autosync: aligns a text subtitle to the video's audio and saves it as a new external subtitle.
/// </summary>
[Route("Visularr/Subtitles")]
[Authorize]
public partial class VisularrSubtitlesController : BaseJellyfinApiController
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly ISubtitleEncoder _subtitleEncoder;
    private readonly ISubtitleManager _subtitleManager;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly SubtitleAligner _aligner;
    private readonly ILogger<VisularrSubtitlesController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="VisularrSubtitlesController"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="mediaSourceManager">Instance of the <see cref="IMediaSourceManager"/> interface.</param>
    /// <param name="subtitleEncoder">Instance of the <see cref="ISubtitleEncoder"/> interface.</param>
    /// <param name="subtitleManager">Instance of the <see cref="ISubtitleManager"/> interface.</param>
    /// <param name="providerManager">Instance of the <see cref="IProviderManager"/> interface.</param>
    /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/> interface.</param>
    /// <param name="aligner">Instance of the <see cref="SubtitleAligner"/>.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{VisularrSubtitlesController}"/> interface.</param>
    public VisularrSubtitlesController(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IMediaSourceManager mediaSourceManager,
        ISubtitleEncoder subtitleEncoder,
        ISubtitleManager subtitleManager,
        IProviderManager providerManager,
        IFileSystem fileSystem,
        SubtitleAligner aligner,
        ILogger<VisularrSubtitlesController> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _mediaSourceManager = mediaSourceManager;
        _subtitleEncoder = subtitleEncoder;
        _subtitleManager = subtitleManager;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _aligner = aligner;
        _logger = logger;
    }

    /// <summary>
    /// Aligns a text subtitle to the video's audio and saves the result as a new external subtitle, "&lt;lang&gt; (synced)".
    /// </summary>
    /// <param name="itemId">The video.</param>
    /// <param name="body">The media source and subtitle stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Synced; the new subtitle's stream index.</response>
    /// <response code="400">Not a text subtitle, or the video is not a local file.</response>
    /// <response code="403">The user may not play this video.</response>
    /// <response code="404">The video, media source or subtitle was not found.</response>
    /// <response code="429">The most syncs that run at once are running.</response>
    /// <response code="501">alass is not installed on this platform.</response>
    /// <response code="504">The sync timed out.</response>
    /// <returns>The <see cref="SubtitleSyncResultDto"/>.</returns>
    [HttpPost("{itemId}/Sync")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<SubtitleSyncResultDto>> Sync(
        [FromRoute, Required] Guid itemId,
        [FromBody, Required] SubtitleSyncDto body,
        CancellationToken cancellationToken)
    {
        if (_aligner.AlignerPath is null)
        {
            return StatusCode(StatusCodes.Status501NotImplemented, "Autosync is not available on this platform");
        }

        User? user = _userManager.GetUserById(User.GetUserId());
        if (user is null)
        {
            return Unauthorized();
        }

        Video? video = _libraryManager.GetItemById<Video>(itemId, user);
        if (video is null)
        {
            return NotFound("Video not found");
        }

        if (video.GetPlayAccess(user) != PlayAccess.Full)
        {
            return Forbid();
        }

        MediaSourceInfo? source = _mediaSourceManager.GetStaticMediaSources(video, false, user)
            .FirstOrDefault(s => string.Equals(s.Id, body.MediaSourceId, StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            return NotFound("Media source not found");
        }

        MediaStream? stream = source.MediaStreams.FirstOrDefault(s => s.Type == MediaStreamType.Subtitle && s.Index == body.StreamIndex);
        if (stream is null)
        {
            return NotFound("Subtitle not found");
        }

        if (!stream.IsTextSubtitleStream)
        {
            return BadRequest("Only text subtitles can be synced.");
        }

        if (source.Protocol != MediaProtocol.File || string.IsNullOrEmpty(source.Path))
        {
            return BadRequest("Only local video files can be synced.");
        }

        if (!_aligner.TryEnter())
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, "Another sync is running. Try again in a moment.");
        }

        try
        {
            HashSet<string> before = ExternalSubtitles(video);
            byte[] synced;
            Stream input = await _subtitleEncoder.GetSubtitles(video, source.Id, stream.Index, "srt", 0, 0, false, cancellationToken).ConfigureAwait(false);
            await using (input.ConfigureAwait(false))
            {
                synced = await _aligner.AlignAsync(source.Path, input, cancellationToken).ConfigureAwait(false);
            }

            // Jellyfin's own save: next to the media when the library saves subtitles there and it is writable, else in its metadata folder.
            await _subtitleManager.UploadSubtitle(video, new SubtitleResponse
            {
                Format = "srt",
                Language = SyncedLanguage(stream.Language),
                IsForced = stream.IsForced,
                IsHearingImpaired = stream.IsHearingImpaired,
                Stream = new MemoryStream(synced),
            }).ConfigureAwait(false);
            await _providerManager.RefreshSingleItem(video, new MetadataRefreshOptions(new DirectoryService(_fileSystem)), cancellationToken).ConfigureAwait(false);

            int? index = Streams(video)
                .FirstOrDefault(s => s.Type == MediaStreamType.Subtitle && s.IsExternal && s.Path is not null && !before.Contains(s.Path))?.Index;
            return new SubtitleSyncResultDto { StreamIndex = index };
        }
        catch (TimeoutException ex)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Autosync of {ItemId} stream {Index} failed", itemId, stream.Index);
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
        finally
        {
            _aligner.Release();
        }
    }

    /// <summary>
    /// The language part of the saved file name. Jellyfin names an uploaded subtitle "video.&lt;language&gt;.srt" and reads
    /// what follows a known language code as the title, so "eng.eng (synced)" gives language eng, title "eng (synced)".
    /// </summary>
    /// <param name="language">The source subtitle's language.</param>
    /// <returns>The language string for <see cref="ISubtitleManager.UploadSubtitle"/>.</returns>
    public static string SyncedLanguage(string? language)
    {
        string? code = language is not null && LanguageCode().IsMatch(language) ? language.ToLowerInvariant() : null;
        return code is null || code == "und" ? "synced" : $"{code}.{code} (synced)";
    }

    private IReadOnlyList<MediaStream> Streams(Video video)
        => _mediaSourceManager.GetMediaStreams(video.Id);

    private HashSet<string> ExternalSubtitles(Video video)
        => Streams(video)
            .Where(s => s.Type == MediaStreamType.Subtitle && s.IsExternal && s.Path is not null)
            .Select(s => s.Path!)
            .ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})?$")]
    private static partial Regex LanguageCode();
}
