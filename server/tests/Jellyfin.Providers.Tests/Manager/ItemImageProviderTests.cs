using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Providers;
using MediaBrowser.Providers.Manager;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Providers.Tests.Manager
{
    public partial class ItemImageProviderTests
    {
        private static readonly CompositeFormat _testDataImagePath = CompositeFormat.Parse("Test Data/Images/blank{0}.jpg");

        [GeneratedRegex("[0-9]+")]
        private static partial Regex NumbersRegex();

        [Fact]
        public void ValidateImages_PhotoEmptyProviders_NoChange()
        {
            ItemImageProvider itemImageProvider = GetItemImageProvider(null, null);
            bool changed = itemImageProvider.ValidateImages(new Photo(), Enumerable.Empty<ILocalImageProvider>(), null);

            Assert.False(changed);
        }

        [Fact]
        public void ValidateImages_EmptyItemEmptyProviders_NoChange()
        {
            ValidateImages_Test(ImageType.Primary, 0, true, 0, false, 0);
        }

        public static TheoryData<ImageType, int> GetImageTypesWithCount()
        {
            TheoryData<ImageType, int> theoryTypes = new TheoryData<ImageType, int>
            {
                // minimal test cases that hit different handling
                { ImageType.Primary, 1 },
                { ImageType.Backdrop, 2 }
            };

            return theoryTypes;
        }

        [Theory]
        [MemberData(nameof(GetImageTypesWithCount))]
        public void ValidateImages_EmptyItemAndPopulatedProviders_AddsImages(ImageType imageType, int imageCount)
        {
            ValidateImages_Test(imageType, 0, true, imageCount, true, imageCount);
        }

        [Theory]
        [MemberData(nameof(GetImageTypesWithCount))]
        public void ValidateImages_PopulatedItemWithGoodPathsAndEmptyProviders_NoChange(ImageType imageType, int imageCount)
        {
            ValidateImages_Test(imageType, imageCount, true, 0, false, imageCount);
        }

        [Theory]
        [MemberData(nameof(GetImageTypesWithCount))]
        public void ValidateImages_PopulatedItemWithBadPathsAndEmptyProviders_RemovesImage(ImageType imageType, int imageCount)
        {
            ValidateImages_Test(imageType, imageCount, false, 0, true, 0);
        }

        private void ValidateImages_Test(ImageType imageType, int initialImageCount, bool initialPathsValid, int providerImageCount, bool expectedChange, int expectedImageCount)
        {
            Video item = GetItemWithImages(imageType, initialImageCount, initialPathsValid);

            ILocalImageProvider imageProvider = GetImageProvider(imageType, providerImageCount, true);

            ItemImageProvider itemImageProvider = GetItemImageProvider(null, null);
            bool actualChange = itemImageProvider.ValidateImages(item, new[] { imageProvider }, null);

            Assert.Equal(expectedChange, actualChange);
            Assert.Equal(expectedImageCount, item.GetImages(imageType).Count());
        }

        [Fact]
        public void MergeImages_EmptyItemNewImagesEmpty_NoChange()
        {
            ItemImageProvider itemImageProvider = GetItemImageProvider(null, null);
            bool changed = itemImageProvider.MergeImages(new Video(), Array.Empty<LocalImageInfo>(), new ImageRefreshOptions(Mock.Of<IDirectoryService>()));

            Assert.False(changed);
        }

        [Theory]
        [MemberData(nameof(GetImageTypesWithCount))]
        public void MergeImages_PopulatedItemWithGoodPathsAndPopulatedNewImages_AddsUpdatesImages(ImageType imageType, int imageCount)
        {
            // valid and not valid paths - should replace the valid paths with the invalid ones
            Video item = GetItemWithImages(imageType, imageCount, true);
            LocalImageInfo[] images = GetImages(imageType, imageCount, false);

            ItemImageProvider itemImageProvider = GetItemImageProvider(null, null);
            bool changed = itemImageProvider.MergeImages(item, images, new ImageRefreshOptions(Mock.Of<IDirectoryService>()));

            Assert.True(changed);
            // adds for types that allow multiple, replaces singular type images
            if (item.AllowsMultipleImages(imageType))
            {
                Assert.Equal(imageCount * 2, item.GetImages(imageType).Count());
            }
            else
            {
                Assert.Single(item.GetImages(imageType));
                Assert.Same(images[0].FileInfo.FullName, item.GetImages(imageType).First().Path);
            }
        }

        [Theory]
        [InlineData(ImageType.Primary, 1, false)]
        [InlineData(ImageType.Backdrop, 2, false)]
        [InlineData(ImageType.Primary, 1, true)]
        [InlineData(ImageType.Backdrop, 2, true)]
        public void MergeImages_PopulatedItemWithGoodPathsAndSameNewImages_ResetIfTimeChanges(ImageType imageType, int imageCount, bool updateTime)
        {
            DateTime oldTime = new DateTime(1970, 1, 1);
            DateTime updatedTime = updateTime ? new DateTime(2021, 1, 1) : oldTime;

            Mock<IFileSystem> fileSystem = new Mock<IFileSystem>();
            fileSystem.Setup(fs => fs.GetLastWriteTimeUtc(It.IsAny<FileSystemMetadata>()))
                .Returns(updatedTime);
            BaseItem.FileSystem = fileSystem.Object;

            // all valid paths - matching for strictly updating
            Video item = GetItemWithImages(imageType, imageCount, true);
            // set size to non-zero to allow for image size reset to occur
            foreach (ItemImageInfo? image in item.GetImages(imageType))
            {
                image.DateModified = oldTime;
                image.Height = 1;
                image.Width = 1;
            }

            LocalImageInfo[] images = GetImages(imageType, imageCount, true);

            ItemImageProvider itemImageProvider = GetItemImageProvider(null, fileSystem);
            bool changed = itemImageProvider.MergeImages(item, images, new ImageRefreshOptions(Mock.Of<IDirectoryService>()));

            if (updateTime)
            {
                Assert.True(changed);
                // before and after paths are the same, verify updated by size reset to 0
                ItemImageInfo[] typedImages = item.GetImages(imageType).ToArray();
                Assert.Equal(imageCount, typedImages.Length);
                foreach (ItemImageInfo? image in typedImages)
                {
                    Assert.Equal(updatedTime, image.DateModified);
                    Assert.Equal(0, image.Height);
                    Assert.Equal(0, image.Width);
                }
            }
            else
            {
                Assert.False(changed);
            }
        }

        [Theory]
        [InlineData(ImageType.Primary, 0)]
        [InlineData(ImageType.Primary, 1)]
        [InlineData(ImageType.Backdrop, 2)]
        public void RemoveImages_DeletesImages_WhenFound(ImageType imageType, int imageCount)
        {
            Video item = GetItemWithImages(imageType, imageCount, false);

            Mock<IFileSystem> mockFileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            if (imageCount > 0)
            {
                mockFileSystem.Setup(fs => fs.DeleteFile("invalid path 0"))
                    .Verifiable();
            }

            if (imageCount > 1)
            {
                mockFileSystem.Setup(fs => fs.DeleteFile("invalid path 1"))
                    .Verifiable();
            }

            ItemImageProvider itemImageProvider = GetItemImageProvider(Mock.Of<IProviderManager>(), mockFileSystem);
            bool result = itemImageProvider.RemoveImages(item);

            Assert.Equal(imageCount != 0, result);
            Assert.Empty(item.GetImages(imageType));
            mockFileSystem.Verify();
        }

        [Theory]
        [InlineData(ImageType.Primary, 1, false)]
        [InlineData(ImageType.Backdrop, 2, false)]
        [InlineData(ImageType.Primary, 1, true)]
        [InlineData(ImageType.Backdrop, 2, true)]
        public async Task RefreshImages_PopulatedItemPopulatedProviderDynamic_UpdatesImagesIfForced(ImageType imageType, int imageCount, bool forceRefresh)
        {
            Video item = GetItemWithImages(imageType, imageCount, false);

            LibraryOptions libraryOptions = GetLibraryOptions(item, imageType, imageCount);

            DynamicImageResponse imageResponse = new DynamicImageResponse
            {
                HasImage = true,
                Format = ImageFormat.Jpg,
                Path = "url path",
                Protocol = MediaProtocol.Http
            };

            Mock<IDynamicImageProvider> dynamicProvider = new Mock<IDynamicImageProvider>(MockBehavior.Strict);
            dynamicProvider.Setup(rp => rp.Name).Returns("MockDynamicProvider");
            dynamicProvider.Setup(rp => rp.GetSupportedImages(item))
                .Returns(new[] { imageType });
            dynamicProvider.Setup(rp => rp.GetImage(item, imageType, It.IsAny<CancellationToken>()))
                .ReturnsAsync(imageResponse);

            ImageRefreshOptions refreshOptions = forceRefresh
                ? new ImageRefreshOptions(Mock.Of<IDirectoryService>())
                {
                    ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                    ReplaceAllImages = true
                }
                : new ImageRefreshOptions(Mock.Of<IDirectoryService>());

            ItemImageProvider itemImageProvider = GetItemImageProvider(null, new Mock<IFileSystem>());
            RefreshResult result = await itemImageProvider.RefreshImages(item, libraryOptions, new List<IImageProvider> { dynamicProvider.Object }, refreshOptions, CancellationToken.None);

            Assert.Equal(forceRefresh, result.UpdateType.HasFlag(ItemUpdateType.ImageUpdate));
            if (forceRefresh)
            {
                // replaces multi-types
                Assert.Single(item.GetImages(imageType));
            }
            else
            {
                // adds to multi-types if room
                Assert.Equal(imageCount, item.GetImages(imageType).Count());
            }
        }

        [Theory]
        [InlineData(ImageType.Primary, 1, true, MediaProtocol.Http)]
        [InlineData(ImageType.Backdrop, 2, true, MediaProtocol.Http)]
        [InlineData(ImageType.Primary, 1, true, MediaProtocol.File)]
        [InlineData(ImageType.Backdrop, 2, true, MediaProtocol.File)]
        [InlineData(ImageType.Primary, 1, false, MediaProtocol.File)]
        [InlineData(ImageType.Backdrop, 2, false, MediaProtocol.File)]
        public async Task RefreshImages_EmptyItemPopulatedProviderDynamic_AddsImages(ImageType imageType, int imageCount, bool responseHasPath, MediaProtocol protocol)
        {
            // Has to exist for querying DateModified time on file, results stored but not checked so not populating
            BaseItem.FileSystem = Mock.Of<IFileSystem>();

            Video item = new Video();

            LibraryOptions libraryOptions = GetLibraryOptions(item, imageType, imageCount);

            // Path must exist if set: is read in as a stream by AsyncFile.OpenRead
            DynamicImageResponse imageResponse = new DynamicImageResponse
            {
                HasImage = true,
                Format = ImageFormat.Jpg,
                Path = responseHasPath ? string.Format(CultureInfo.InvariantCulture, _testDataImagePath, 0) : null,
                Protocol = protocol
            };

            Mock<IDynamicImageProvider> dynamicProvider = new Mock<IDynamicImageProvider>(MockBehavior.Strict);
            dynamicProvider.Setup(rp => rp.Name).Returns("MockDynamicProvider");
            dynamicProvider.Setup(rp => rp.GetSupportedImages(item))
                .Returns(new[] { imageType });
            dynamicProvider.Setup(rp => rp.GetImage(item, imageType, It.IsAny<CancellationToken>()))
                .ReturnsAsync(imageResponse);

            ImageRefreshOptions refreshOptions = new ImageRefreshOptions(Mock.Of<IDirectoryService>());

            Mock<IProviderManager> providerManager = new Mock<IProviderManager>(MockBehavior.Strict);
            providerManager.Setup(pm => pm.SaveImage(item, It.IsAny<Stream>(), It.IsAny<string>(), imageType, null, It.IsAny<CancellationToken>()))
                .Callback<BaseItem, Stream, string, ImageType, int?, CancellationToken>((callbackItem, _, _, callbackType, _, _) => callbackItem.SetImagePath(callbackType, 0, new FileSystemMetadata()))
                .Returns(Task.CompletedTask);
            providerManager.Setup(pm => pm.SaveImage(item, It.IsAny<string>(), It.IsAny<string>(), imageType, null, null, It.IsAny<CancellationToken>()))
                .Callback<BaseItem, string, string, ImageType, int?, bool?, CancellationToken>((callbackItem, _, _, callbackType, _, _, _) => callbackItem.SetImagePath(callbackType, 0, new FileSystemMetadata()))
                .Returns(Task.CompletedTask);
            ItemImageProvider itemImageProvider = GetItemImageProvider(providerManager.Object, null);
            RefreshResult result = await itemImageProvider.RefreshImages(item, libraryOptions, new List<IImageProvider> { dynamicProvider.Object }, refreshOptions, CancellationToken.None);

            Assert.True(result.UpdateType.HasFlag(ItemUpdateType.ImageUpdate));
            // dynamic provider unable to return multiple images
            Assert.Single(item.GetImages(imageType));
            if (protocol == MediaProtocol.Http)
            {
                Assert.Equal(imageResponse.Path, item.GetImagePath(imageType, 0));
            }
        }

        [Theory]
        [InlineData(ImageType.Primary, 1, false)]
        [InlineData(ImageType.Backdrop, 1, false)]
        [InlineData(ImageType.Backdrop, 2, false)]
        [InlineData(ImageType.Primary, 1, true)]
        [InlineData(ImageType.Backdrop, 1, true)]
        [InlineData(ImageType.Backdrop, 2, true)]
        public async Task RefreshImages_PopulatedItemPopulatedProviderRemote_UpdatesImagesIfForced(ImageType imageType, int imageCount, bool forceRefresh)
        {
            Video item = GetItemWithImages(imageType, imageCount, false);

            LibraryOptions libraryOptions = GetLibraryOptions(item, imageType, imageCount);

            Mock<IRemoteImageProvider> remoteProvider = new Mock<IRemoteImageProvider>(MockBehavior.Strict);
            remoteProvider.Setup(rp => rp.Name).Returns("MockRemoteProvider");
            remoteProvider.Setup(rp => rp.GetSupportedImages(item))
                .Returns(new[] { imageType });

            ImageRefreshOptions refreshOptions = forceRefresh
                ? new ImageRefreshOptions(Mock.Of<IDirectoryService>())
                {
                    ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                    ReplaceAllImages = true
                }
                : new ImageRefreshOptions(Mock.Of<IDirectoryService>());

            RemoteImageInfo[] remoteInfo = new RemoteImageInfo[imageCount];
            for (int i = 0; i < imageCount; i++)
            {
                remoteInfo[i] = new RemoteImageInfo
                {
                    Type = imageType,
                    Url = "image url " + i
                };
            }

            Mock<IProviderManager> providerManager = new Mock<IProviderManager>(MockBehavior.Strict);
            providerManager.Setup(pm => pm.GetAvailableRemoteImages(It.IsAny<BaseItem>(), It.IsAny<RemoteImageQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(remoteInfo);
            ItemImageProvider itemImageProvider = GetItemImageProvider(providerManager.Object, new Mock<IFileSystem>());
            RefreshResult result = await itemImageProvider.RefreshImages(item, libraryOptions, new List<IImageProvider> { remoteProvider.Object }, refreshOptions, CancellationToken.None);

            Assert.Equal(forceRefresh, result.UpdateType.HasFlag(ItemUpdateType.ImageUpdate));
            Assert.Equal(imageCount, item.GetImages(imageType).Count());
            foreach (ItemImageInfo? image in item.GetImages(imageType))
            {
                if (forceRefresh)
                {
                    Assert.Matches("image url [0-9]", image.Path);
                }
                else
                {
                    Assert.DoesNotMatch("image url [0-9]", image.Path);
                }
            }
        }

        [Theory]
        [InlineData(ImageType.Primary, 0, false)] // singular type only fetches if type is missing from item, no caching
        [InlineData(ImageType.Backdrop, 0, false)] // empty item, no cache to check
        [InlineData(ImageType.Backdrop, 1, false)] // populated item, cached so no download
        [InlineData(ImageType.Backdrop, 1, true)] // populated item, forced to download
        public async Task RefreshImages_NonStubItemPopulatedProviderRemote_DownloadsIfNecessary(ImageType imageType, int initialImageCount, bool fullRefresh)
        {
            int targetImageCount = 1;

            // Set path and media source manager so images will be downloaded (EnableImageStub will return false)
            Video item = GetItemWithImages(imageType, initialImageCount, false);
            item.Path = "non-empty path";
            BaseItem.MediaSourceManager = Mock.Of<IMediaSourceManager>();

            // seek 2 so it won't short-circuit out of downloading when populated
            LibraryOptions libraryOptions = GetLibraryOptions(item, imageType, 2);

            const string Content = "Content";
            Mock<IRemoteImageProvider> remoteProvider = new Mock<IRemoteImageProvider>(MockBehavior.Strict);
            remoteProvider.Setup(rp => rp.Name).Returns("MockRemoteProvider");
            remoteProvider.Setup(rp => rp.GetSupportedImages(item))
                .Returns(new[] { imageType });
            remoteProvider.Setup(rp => rp.GetImageResponse(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string url, CancellationToken _) => new HttpResponseMessage
                {
                    ReasonPhrase = url,
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(Content, Encoding.UTF8, MediaTypeNames.Image.Jpeg)
                });

            ImageRefreshOptions refreshOptions = fullRefresh
                ? new ImageRefreshOptions(Mock.Of<IDirectoryService>())
                {
                    ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                    ReplaceAllImages = true
                }
                : new ImageRefreshOptions(Mock.Of<IDirectoryService>());

            RemoteImageInfo[] remoteInfo = new RemoteImageInfo[targetImageCount];
            for (int i = 0; i < targetImageCount; i++)
            {
                remoteInfo[i] = new RemoteImageInfo
                {
                    Type = imageType,
                    Url = "image url " + i
                };
            }

            Mock<IProviderManager> providerManager = new Mock<IProviderManager>(MockBehavior.Strict);
            providerManager.Setup(pm => pm.GetAvailableRemoteImages(It.IsAny<BaseItem>(), It.IsAny<RemoteImageQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(remoteInfo);
            providerManager.Setup(pm => pm.SaveImage(item, It.IsAny<Stream>(), It.IsAny<string>(), imageType, null, It.IsAny<CancellationToken>()))
                .Callback<BaseItem, Stream, string, ImageType, int?, CancellationToken>((callbackItem, _, _, callbackType, _, _) =>
                    callbackItem.SetImagePath(callbackType, callbackItem.AllowsMultipleImages(callbackType) ? callbackItem.GetImages(callbackType).Count() : 0, new FileSystemMetadata()))
                .Returns(Task.CompletedTask);
            Mock<IFileSystem> fileSystem = new Mock<IFileSystem>();
            // match reported file size to image content length - condition for skipping already downloaded multi-images
            fileSystem.Setup(fs => fs.GetFileInfo(It.IsAny<string>()))
                .Returns(new FileSystemMetadata { Length = Content.Length });
            ItemImageProvider itemImageProvider = GetItemImageProvider(providerManager.Object, fileSystem);
            RefreshResult result = await itemImageProvider.RefreshImages(item, libraryOptions, new List<IImageProvider> { remoteProvider.Object }, refreshOptions, CancellationToken.None);

            Assert.Equal(initialImageCount == 0 || fullRefresh, result.UpdateType.HasFlag(ItemUpdateType.ImageUpdate));
            Assert.Equal(targetImageCount, item.GetImages(imageType).Count());
        }

        [Theory]
        [MemberData(nameof(GetImageTypesWithCount))]
        public async Task RefreshImages_EmptyItemPopulatedProviderRemoteExtras_LimitsImages(ImageType imageType, int imageCount)
        {
            Video item = new Video();

            LibraryOptions libraryOptions = GetLibraryOptions(item, imageType, imageCount);

            Mock<IRemoteImageProvider> remoteProvider = new Mock<IRemoteImageProvider>(MockBehavior.Strict);
            remoteProvider.Setup(rp => rp.Name).Returns("MockRemoteProvider");
            remoteProvider.Setup(rp => rp.GetSupportedImages(item))
                .Returns(new[] { imageType });

            ImageRefreshOptions refreshOptions = new ImageRefreshOptions(Mock.Of<IDirectoryService>());

            // populate remote with double the required images to verify count is trimmed to the library option count
            int remoteInfoCount = imageCount * 2;
            RemoteImageInfo[] remoteInfo = new RemoteImageInfo[remoteInfoCount];
            for (int i = 0; i < remoteInfoCount; i++)
            {
                remoteInfo[i] = new RemoteImageInfo
                {
                    Type = imageType,
                    Url = "image url " + i
                };
            }

            Mock<IProviderManager> providerManager = new Mock<IProviderManager>(MockBehavior.Strict);
            providerManager.Setup(pm => pm.GetAvailableRemoteImages(It.IsAny<BaseItem>(), It.IsAny<RemoteImageQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(remoteInfo);
            ItemImageProvider itemImageProvider = GetItemImageProvider(providerManager.Object, null);
            RefreshResult result = await itemImageProvider.RefreshImages(item, libraryOptions, new List<IImageProvider> { remoteProvider.Object }, refreshOptions, CancellationToken.None);

            Assert.True(result.UpdateType.HasFlag(ItemUpdateType.ImageUpdate));
            List<ItemImageInfo> actualImages = item.GetImages(imageType).ToList();
            Assert.Equal(imageCount, actualImages.Count);
            // images from the provider manager are sorted by preference (earlier images are higher priority) so we can verify that low url numbers are chosen
            foreach (ItemImageInfo image in actualImages)
            {
                int index = int.Parse(NumbersRegex().Match(image.Path).ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture);
                Assert.True(index < imageCount);
            }
        }

        [Theory]
        [MemberData(nameof(GetImageTypesWithCount))]
        public async Task RefreshImages_PopulatedItemEmptyProviderRemoteFullRefresh_DoesntClearImages(ImageType imageType, int imageCount)
        {
            Video item = GetItemWithImages(imageType, imageCount, false);

            LibraryOptions libraryOptions = GetLibraryOptions(item, imageType, imageCount);

            Mock<IRemoteImageProvider> remoteProvider = new Mock<IRemoteImageProvider>(MockBehavior.Strict);
            remoteProvider.Setup(rp => rp.Name).Returns("MockRemoteProvider");
            remoteProvider.Setup(rp => rp.GetSupportedImages(item))
                .Returns(new[] { imageType });

            ImageRefreshOptions refreshOptions = new ImageRefreshOptions(Mock.Of<IDirectoryService>())
            {
                ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                ReplaceAllImages = true
            };

            ItemImageProvider itemImageProvider = GetItemImageProvider(Mock.Of<IProviderManager>(), null);
            RefreshResult result = await itemImageProvider.RefreshImages(item, libraryOptions, new List<IImageProvider> { remoteProvider.Object }, refreshOptions, CancellationToken.None);

            Assert.False(result.UpdateType.HasFlag(ItemUpdateType.ImageUpdate));
            Assert.Equal(imageCount, item.GetImages(imageType).Count());
        }

        [Theory]
        [InlineData(9, false)]
        [InlineData(10, true)]
        [InlineData(null, true)]
        public async Task RefreshImages_ProviderRemote_FiltersByWidth(int? remoteImageWidth, bool expectedToUpdate)
        {
            ImageType imageType = ImageType.Primary;

            Video item = new Video();

            LibraryOptions libraryOptions = new LibraryOptions
            {
                TypeOptions = new[]
                {
                    new TypeOptions
                    {
                        Type = item.GetType().Name,
                        ImageOptions = new[]
                        {
                            new ImageOption
                            {
                                Type = imageType,
                                MinWidth = 10
                            }
                        }
                    }
                }
            };

            Mock<IRemoteImageProvider> remoteProvider = new Mock<IRemoteImageProvider>(MockBehavior.Strict);
            remoteProvider.Setup(rp => rp.Name).Returns("MockRemoteProvider");
            remoteProvider.Setup(rp => rp.GetSupportedImages(item))
                .Returns(new[] { imageType });

            ImageRefreshOptions refreshOptions = new ImageRefreshOptions(Mock.Of<IDirectoryService>());

            // set width on image from remote
            RemoteImageInfo[] remoteInfo = new[]
            {
                new RemoteImageInfo()
                {
                    Type = imageType,
                    Url = "image url",
                    Width = remoteImageWidth
                }
            };

            Mock<IProviderManager> providerManager = new Mock<IProviderManager>(MockBehavior.Strict);
            providerManager.Setup(pm => pm.GetAvailableRemoteImages(It.IsAny<BaseItem>(), It.IsAny<RemoteImageQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(remoteInfo);
            ItemImageProvider itemImageProvider = GetItemImageProvider(providerManager.Object, null);
            RefreshResult result = await itemImageProvider.RefreshImages(item, libraryOptions, new List<IImageProvider> { remoteProvider.Object }, refreshOptions, CancellationToken.None);

            Assert.Equal(expectedToUpdate, result.UpdateType.HasFlag(ItemUpdateType.ImageUpdate));
        }

        private static ItemImageProvider GetItemImageProvider(IProviderManager? providerManager, Mock<IFileSystem>? mockFileSystem)
        {
            // strict to ensure this isn't accidentally used where a prepared mock is intended
            providerManager ??= Mock.Of<IProviderManager>(MockBehavior.Strict);

            // BaseItem.ValidateImages depends on the directory service being able to list directory contents, give it the expected valid file paths
            mockFileSystem ??= new Mock<IFileSystem>(MockBehavior.Strict);
            mockFileSystem.Setup(fs => fs.GetFilePaths(It.IsAny<string>(), It.IsAny<bool>()))
                .Returns(new[]
                {
                    string.Format(CultureInfo.InvariantCulture, _testDataImagePath, 0),
                    string.Format(CultureInfo.InvariantCulture, _testDataImagePath, 1)
                });

            return new ItemImageProvider(new NullLogger<ItemImageProvider>(), providerManager, mockFileSystem.Object);
        }

        private static Video GetItemWithImages(ImageType type, int count, bool validPaths)
        {
            // Has to exist for querying DateModified time on file, results stored but not checked so not populating
            BaseItem.FileSystem ??= Mock.Of<IFileSystem>();

            Mock<Video> item = new Mock<Video>
            {
                CallBase = true
            };
            item.Setup(m => m.IsSaveLocalMetadataEnabled()).Returns(false);
            item.Setup(m => m.GetInternalMetadataPath()).Returns(string.Empty);

            string path = validPaths ? _testDataImagePath.Format : "invalid path {0}";
            for (int i = 0; i < count; i++)
            {
                item.Object.SetImagePath(type, i, new FileSystemMetadata
                {
                    FullName = string.Format(CultureInfo.InvariantCulture, path, i),
                });
            }

            return item.Object;
        }

        private static ILocalImageProvider GetImageProvider(ImageType type, int count, bool validPaths)
        {
            LocalImageInfo[] images = GetImages(type, count, validPaths);

            Mock<ILocalImageProvider> imageProvider = new Mock<ILocalImageProvider>();
            imageProvider.Setup(ip => ip.GetImages(It.IsAny<BaseItem>(), It.IsAny<IDirectoryService>()))
                .Returns(images);
            return imageProvider.Object;
        }

        /// <summary>
        /// Creates a list of <see cref="LocalImageInfo"/> references of the specified type and size, optionally pointing to files that exist.
        /// </summary>
        private static LocalImageInfo[] GetImages(ImageType type, int count, bool validPaths)
        {
            string path = validPaths ? _testDataImagePath.Format : "invalid path {0}";
            LocalImageInfo[] images = new LocalImageInfo[count];
            for (int i = 0; i < count; i++)
            {
                images[i] = new LocalImageInfo
                {
                    Type = type,
                    FileInfo = new FileSystemMetadata
                    {
                        FullName = string.Format(CultureInfo.InvariantCulture, path, i)
                    }
                };
            }

            return images;
        }

        /// <summary>
        /// Generates a <see cref="LibraryOptions"/> object that will allow for the requested number of images for the target type.
        /// </summary>
        private static LibraryOptions GetLibraryOptions(BaseItem item, ImageType type, int count)
        {
            return new LibraryOptions
            {
                TypeOptions = new[]
                {
                    new TypeOptions
                    {
                        Type = item.GetType().Name,
                        ImageOptions = new[]
                        {
                            new ImageOption
                            {
                                Type = type,
                                Limit = count,
                            }
                        }
                    }
                }
            };
        }
    }
}
