#nullable disable

#pragma warning disable CS1591

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.LiveTv.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.LiveTv;
using Microsoft.Extensions.Logging;

namespace Jellyfin.LiveTv.TunerHosts
{
    public abstract class BaseTunerHost
    {
        private readonly ConcurrentDictionary<string, List<ChannelInfo>> _cache;

        protected BaseTunerHost(IServerConfigurationManager config, ILogger<BaseTunerHost> logger, IFileSystem fileSystem)
        {
            Config = config;
            Logger = logger;
            FileSystem = fileSystem;
            _cache = new ConcurrentDictionary<string, List<ChannelInfo>>();
        }

        protected IServerConfigurationManager Config { get; }

        protected ILogger<BaseTunerHost> Logger { get; }

        protected IFileSystem FileSystem { get; }

        public virtual bool IsSupported => true;

        public abstract string Type { get; }

        protected virtual string ChannelIdPrefix => Type + "_";

        protected abstract Task<List<ChannelInfo>> GetChannelsInternal(TunerHostInfo tuner, CancellationToken cancellationToken);

        public async Task<List<ChannelInfo>> GetChannels(TunerHostInfo tuner, bool enableCache, CancellationToken cancellationToken)
        {
            string key = tuner.Id;

            if (enableCache && !string.IsNullOrEmpty(key) && _cache.TryGetValue(key, out List<ChannelInfo> cache))
            {
                return cache;
            }

            List<ChannelInfo> list = await GetChannelsInternal(tuner, cancellationToken).ConfigureAwait(false);
            // logger.LogInformation("Channels from {0}: {1}", tuner.Url, JsonSerializer.SerializeToString(list));

            if (!string.IsNullOrEmpty(key) && list.Count > 0)
            {
                _cache[key] = list;
            }

            return list;
        }

        protected virtual IList<TunerHostInfo> GetTunerHosts()
        {
            return Config.GetLiveTvConfiguration().TunerHosts
                .Where(i => string.Equals(i.Type, Type, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public async Task<List<ChannelInfo>> GetChannels(bool enableCache, CancellationToken cancellationToken)
        {
            List<ChannelInfo> list = new List<ChannelInfo>();

            IList<TunerHostInfo> hosts = GetTunerHosts();

            foreach (TunerHostInfo host in hosts)
            {
                string channelCacheFile = Path.Combine(Config.ApplicationPaths.CachePath, host.Id + "_channels");

                try
                {
                    List<ChannelInfo> channels = await GetChannels(host, enableCache, cancellationToken).ConfigureAwait(false);
                    List<ChannelInfo> newChannels = channels.Where(i => !list.Any(l => string.Equals(i.Id, l.Id, StringComparison.OrdinalIgnoreCase))).ToList();

                    list.AddRange(newChannels);

                    if (!enableCache)
                    {
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(channelCacheFile));
                            FileStream writeStream = AsyncFile.OpenWrite(channelCacheFile);
                            await using (writeStream.ConfigureAwait(false))
                            {
                                await JsonSerializer.SerializeAsync(writeStream, channels, cancellationToken: cancellationToken).ConfigureAwait(false);
                            }
                        }
                        catch (IOException)
                        {
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error getting channel list");

                    if (enableCache)
                    {
                        try
                        {
                            FileStream readStream = AsyncFile.OpenRead(channelCacheFile);
                            await using (readStream.ConfigureAwait(false))
                            {
                                List<ChannelInfo> channels = await JsonSerializer
                                    .DeserializeAsync<List<ChannelInfo>>(readStream, cancellationToken: cancellationToken)
                                    .ConfigureAwait(false);
                                list.AddRange(channels);
                            }
                        }
                        catch (IOException)
                        {
                        }
                    }
                }
            }

            return list;
        }

        protected abstract Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(TunerHostInfo tuner, ChannelInfo channel, CancellationToken cancellationToken);

        public async Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(string channelId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(channelId);

            if (IsValidChannelId(channelId))
            {
                IList<TunerHostInfo> hosts = GetTunerHosts();

                foreach (TunerHostInfo host in hosts)
                {
                    try
                    {
                        List<ChannelInfo> channels = await GetChannels(host, true, cancellationToken).ConfigureAwait(false);
                        ChannelInfo channelInfo = channels.FirstOrDefault(i => string.Equals(i.Id, channelId, StringComparison.OrdinalIgnoreCase));

                        if (channelInfo is not null)
                        {
                            return await GetChannelStreamMediaSources(host, channelInfo, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error getting channels");
                    }
                }
            }

            return new List<MediaSourceInfo>();
        }

        protected abstract Task<ILiveStream> GetChannelStream(TunerHostInfo tunerHost, ChannelInfo channel, string streamId, IList<ILiveStream> currentLiveStreams, CancellationToken cancellationToken);

        public async Task<ILiveStream> GetChannelStream(string channelId, string streamId, IList<ILiveStream> currentLiveStreams, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(channelId);

            if (!IsValidChannelId(channelId))
            {
                throw new FileNotFoundException();
            }

            IList<TunerHostInfo> hosts = GetTunerHosts();

            List<Tuple<TunerHostInfo, ChannelInfo>> hostsWithChannel = new List<Tuple<TunerHostInfo, ChannelInfo>>();

            foreach (TunerHostInfo host in hosts)
            {
                try
                {
                    List<ChannelInfo> channels = await GetChannels(host, true, cancellationToken).ConfigureAwait(false);
                    ChannelInfo channelInfo = channels.FirstOrDefault(i => string.Equals(i.Id, channelId, StringComparison.OrdinalIgnoreCase));

                    if (channelInfo is not null)
                    {
                        hostsWithChannel.Add(new Tuple<TunerHostInfo, ChannelInfo>(host, channelInfo));
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error getting channels");
                }
            }

            foreach (Tuple<TunerHostInfo, ChannelInfo> hostTuple in hostsWithChannel)
            {
                TunerHostInfo host = hostTuple.Item1;
                ChannelInfo channelInfo = hostTuple.Item2;

                try
                {
                    ILiveStream liveStream = await GetChannelStream(host, channelInfo, streamId, currentLiveStreams, cancellationToken).ConfigureAwait(false);
                    DateTime startTime = DateTime.UtcNow;
                    await liveStream.Open(cancellationToken).ConfigureAwait(false);
                    DateTime endTime = DateTime.UtcNow;
                    Logger.LogInformation("Live stream opened after {0}ms", (endTime - startTime).TotalMilliseconds);
                    return liveStream;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error opening tuner");
                }
            }

            throw new LiveTvConflictException("Unable to find host to play channel");
        }

        protected virtual bool IsValidChannelId(string channelId)
        {
            ArgumentException.ThrowIfNullOrEmpty(channelId);

            return channelId.StartsWith(ChannelIdPrefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
