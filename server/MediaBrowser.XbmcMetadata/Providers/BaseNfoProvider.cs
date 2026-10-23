#pragma warning disable CS1591

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.XbmcMetadata.Savers;

namespace MediaBrowser.XbmcMetadata.Providers
{
    public abstract class BaseNfoProvider<T> : ILocalMetadataProvider<T>, IHasItemChangeMonitor
        where T : BaseItem, new()
    {
        private readonly IFileSystem _fileSystem;

        protected BaseNfoProvider(IFileSystem fileSystem)
        {
            _fileSystem = fileSystem;
        }

        /// <inheritdoc />
        public string Name => BaseNfoSaver.SaverName;

        /// <inheritdoc />
        public Task<MetadataResult<T>> GetMetadata(
            ItemInfo info,
            IDirectoryService directoryService,
            CancellationToken cancellationToken)
        {
            MetadataResult<T> result = new MetadataResult<T>();

            FileSystemMetadata? file = GetXmlFile(info, directoryService);

            if (file is null)
            {
                return Task.FromResult(result);
            }

            string path = file.FullName;

            try
            {
                result.Item = new T
                {
                    IndexNumber = info.IndexNumber
                };

                Fetch(result, path, cancellationToken);
                result.HasMetadata = true;
            }
            catch (FileNotFoundException)
            {
                result.HasMetadata = false;
            }
            catch (IOException)
            {
                result.HasMetadata = false;
            }

            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public bool HasChanged(BaseItem item, IDirectoryService directoryService)
        {
            FileSystemMetadata? file = GetXmlFile(new ItemInfo(item), directoryService);

            if (file?.Exists is not true)
            {
                return false;
            }

            DateTime fileTime = _fileSystem.GetLastWriteTimeUtc(file);

            // 1 minute tolerance to avoid detecting our own file writes
            return (fileTime - item.DateLastSaved) > TimeSpan.FromMinutes(1);
        }

        protected abstract void Fetch(MetadataResult<T> result, string path, CancellationToken cancellationToken);

        protected abstract FileSystemMetadata? GetXmlFile(ItemInfo info, IDirectoryService directoryService);
    }
}
