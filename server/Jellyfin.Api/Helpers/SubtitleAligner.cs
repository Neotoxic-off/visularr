using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Api.Helpers;

/// <summary>
/// Aligns a text subtitle to a video's audio with alass (alass-cli), run as a child process.
/// </summary>
public sealed class SubtitleAligner : IDisposable
{
    /// <summary>
    /// The most syncs that run at once; each decodes a whole audio track.
    /// </summary>
    public const int MaxConcurrent = 2;

    /// <summary>
    /// The environment variable that points at the alass binary, overriding the PATH lookup.
    /// </summary>
    public const string PathVariable = "VISULARR_ALASS_PATH";

    private const int MaxErrorLength = 500;

    private readonly SemaphoreSlim _slots = new(MaxConcurrent, MaxConcurrent);
    private readonly Func<string> _tempRoot;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly ILogger<SubtitleAligner> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubtitleAligner"/> class.
    /// </summary>
    /// <param name="alignerPath">The alass binary, null when it is not installed.</param>
    /// <param name="tempRoot">The folder that per-sync temp folders go under (the transcode path).</param>
    /// <param name="mediaEncoder">The media encoder, for the bundled ffmpeg and ffprobe alass decodes the audio with.</param>
    /// <param name="timeout">How long one sync may run.</param>
    /// <param name="logger">The logger.</param>
    public SubtitleAligner(string? alignerPath, Func<string> tempRoot, IMediaEncoder mediaEncoder, TimeSpan timeout, ILogger<SubtitleAligner> logger)
    {
        AlignerPath = alignerPath;
        _tempRoot = tempRoot;
        _mediaEncoder = mediaEncoder;
        Timeout = timeout;
        _logger = logger;
    }

    /// <summary>
    /// Gets the alass binary, null when it is not installed (alass has no arm64 release).
    /// </summary>
    public string? AlignerPath { get; }

    /// <summary>
    /// Gets how long one sync may run.
    /// </summary>
    public TimeSpan Timeout { get; }

    /// <summary>
    /// Finds alass: <see cref="PathVariable"/>, else alass-cli on the PATH.
    /// </summary>
    /// <returns>The binary's path, or null.</returns>
    public static string? Locate()
    {
        string? configured = Environment.GetEnvironmentVariable(PathVariable);
        if (!string.IsNullOrEmpty(configured))
        {
            return File.Exists(configured) ? configured : null;
        }

        string name = OperatingSystem.IsWindows() ? "alass-cli.exe" : "alass-cli";
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(folder, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// The alass arguments: the video as the reference, the subtitle to fix, where to write the result.
    /// </summary>
    /// <param name="videoPath">The video.</param>
    /// <param name="inputPath">The subtitle to align.</param>
    /// <param name="outputPath">The aligned subtitle.</param>
    /// <returns>The argument list, passed to the process as is (no shell).</returns>
    public static IReadOnlyList<string> BuildArguments(string videoPath, string inputPath, string outputPath) => [videoPath, inputPath, outputPath];

    /// <summary>
    /// Takes a sync slot without waiting.
    /// </summary>
    /// <returns>Whether a slot was free; release it with <see cref="Release"/>.</returns>
    public bool TryEnter() => _slots.Wait(0);

    /// <summary>
    /// Gives a slot back.
    /// </summary>
    public void Release() => _slots.Release();

    /// <summary>
    /// Aligns an SRT subtitle to the video's audio.
    /// </summary>
    /// <param name="videoPath">The video file, only read.</param>
    /// <param name="subtitle">The subtitle, as SRT.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The aligned SRT.</returns>
    /// <exception cref="TimeoutException">alass ran past <see cref="Timeout"/>.</exception>
    /// <exception cref="InvalidOperationException">alass is not installed or failed.</exception>
    public async Task<byte[]> AlignAsync(string videoPath, Stream subtitle, CancellationToken cancellationToken)
    {
        string aligner = AlignerPath ?? throw new InvalidOperationException("Autosync is not available on this platform");
        string folder = Path.Combine(_tempRoot(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string input = Path.Combine(folder, "in.srt");
            string output = Path.Combine(folder, "out.srt");
            FileStream file = File.Create(input);
            await using (file.ConfigureAwait(false))
            {
                await subtitle.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            ProcessStartInfo start = new ProcessStartInfo(aligner)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = folder,
            };
            foreach (string argument in BuildArguments(videoPath, input, output))
            {
                start.ArgumentList.Add(argument);
            }

            start.Environment["ALASS_FFMPEG_PATH"] = _mediaEncoder.EncoderPath;
            start.Environment["ALASS_FFPROBE_PATH"] = _mediaEncoder.ProbePath;

            using Process process = new Process { StartInfo = start };
            process.Start();
            // Both pipes drained so a chatty alass never blocks on a full buffer.
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            Task<string> stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
            using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(Timeout);
            try
            {
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                process.Kill(true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException($"Autosync took longer than {Timeout.TotalMinutes:0.#} minutes.");
            }

            string errors = await stderr.ConfigureAwait(false);
            await stdout.ConfigureAwait(false);
            if (process.ExitCode != 0 || !File.Exists(output))
            {
                _logger.LogWarning("alass exited with {ExitCode}: {Errors}", process.ExitCode, errors);
                throw new InvalidOperationException("Autosync failed: " + Tail(errors));
            }

            return await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, true);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not remove the autosync temp folder {Folder}", folder);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Could not remove the autosync temp folder {Folder}", folder);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _slots.Dispose();

    private static string Tail(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return "alass gave no reason.";
        }

        return trimmed.Length > MaxErrorLength ? trimmed[^MaxErrorLength..] : trimmed;
    }
}
