using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Moq;
using Xunit;

namespace Jellyfin.Controller.Tests
{
    public class DirectoryServiceTests
    {
        private const string LowerCasePath = "/music/someartist";
        private const string UpperCasePath = "/music/SOMEARTIST";

        private static readonly FileSystemMetadata[] _lowerCaseFileSystemMetadata =
        {
            new()
            {
                FullName = LowerCasePath + "/Artwork",
                IsDirectory = true
            },
            new()
            {
                FullName = LowerCasePath + "/Some Other Folder",
                IsDirectory = true
            },
            new()
            {
                FullName = LowerCasePath + "/Song 2.mp3",
                IsDirectory = false
            },
            new()
            {
                FullName = LowerCasePath + "/Song 3.mp3",
                IsDirectory = false
            }
        };

        private static readonly FileSystemMetadata[] _upperCaseFileSystemMetadata =
        {
            new()
            {
                FullName = UpperCasePath + "/Lyrics",
                IsDirectory = true
            },
            new()
            {
                FullName = UpperCasePath + "/Song 1.mp3",
                IsDirectory = false
            }
        };

        [Fact]
        public void GetFileSystemEntries_GivenPathsWithDifferentCasing_CachesAll()
        {
            Mock<IFileSystem> fileSystemMock = new Mock<IFileSystem>();
            fileSystemMock.Setup(f => f.GetFileSystemEntries(It.Is<string>(x => x == UpperCasePath), false)).Returns(_upperCaseFileSystemMetadata);
            fileSystemMock.Setup(f => f.GetFileSystemEntries(It.Is<string>(x => x == LowerCasePath), false)).Returns(_lowerCaseFileSystemMetadata);
            DirectoryService directoryService = new DirectoryService(fileSystemMock.Object);

            FileSystemMetadata[] upperCaseResult = directoryService.GetFileSystemEntries(UpperCasePath);
            FileSystemMetadata[] lowerCaseResult = directoryService.GetFileSystemEntries(LowerCasePath);

            Assert.Equal(_upperCaseFileSystemMetadata, upperCaseResult);
            Assert.Equal(_lowerCaseFileSystemMetadata, lowerCaseResult);
        }

        [Fact]
        public void GetFiles_GivenPathsWithDifferentCasing_ReturnsCorrectFiles()
        {
            Mock<IFileSystem> fileSystemMock = new Mock<IFileSystem>();
            fileSystemMock.Setup(f => f.GetFileSystemEntries(It.Is<string>(x => x == UpperCasePath), false)).Returns(_upperCaseFileSystemMetadata);
            fileSystemMock.Setup(f => f.GetFileSystemEntries(It.Is<string>(x => x == LowerCasePath), false)).Returns(_lowerCaseFileSystemMetadata);
            DirectoryService directoryService = new DirectoryService(fileSystemMock.Object);

            List<FileSystemMetadata> upperCaseResult = directoryService.GetFiles(UpperCasePath);
            List<FileSystemMetadata> lowerCaseResult = directoryService.GetFiles(LowerCasePath);

            Assert.Equal(_upperCaseFileSystemMetadata.Where(f => !f.IsDirectory), upperCaseResult);
            Assert.Equal(_lowerCaseFileSystemMetadata.Where(f => !f.IsDirectory), lowerCaseResult);
        }

        [Fact]
        public void GetDirectories_GivenPathsWithDifferentCasing_ReturnsCorrectDirectories()
        {
            Mock<IFileSystem> fileSystemMock = new Mock<IFileSystem>();
            fileSystemMock.Setup(f => f.GetFileSystemEntries(It.Is<string>(x => x == UpperCasePath), false)).Returns(_upperCaseFileSystemMetadata);
            fileSystemMock.Setup(f => f.GetFileSystemEntries(It.Is<string>(x => x == LowerCasePath), false)).Returns(_lowerCaseFileSystemMetadata);
            DirectoryService directoryService = new DirectoryService(fileSystemMock.Object);

            List<FileSystemMetadata> upperCaseResult = directoryService.GetDirectories(UpperCasePath);
            List<FileSystemMetadata> lowerCaseResult = directoryService.GetDirectories(LowerCasePath);

            Assert.Equal(_upperCaseFileSystemMetadata.Where(f => f.IsDirectory), upperCaseResult);
            Assert.Equal(_lowerCaseFileSystemMetadata.Where(f => f.IsDirectory), lowerCaseResult);
        }

        [Fact]
        public void GetFile_GivenFilePathsWithDifferentCasing_ReturnsCorrectFile()
        {
            const string lowerCasePath = "/music/someartist/song 1.mp3";
            FileSystemMetadata lowerCaseFileSystemMetadata = new FileSystemMetadata
            {
                FullName = lowerCasePath,
                Exists = true
            };
            const string upperCasePath = "/music/SOMEARTIST/SONG 1.mp3";
            FileSystemMetadata upperCaseFileSystemMetadata = new FileSystemMetadata
            {
                FullName = upperCasePath,
                Exists = false
            };
            Mock<IFileSystem> fileSystemMock = new Mock<IFileSystem>();
            fileSystemMock.Setup(f => f.GetFileSystemInfo(It.Is<string>(x => x == upperCasePath))).Returns(upperCaseFileSystemMetadata);
            fileSystemMock.Setup(f => f.GetFileSystemInfo(It.Is<string>(x => x == lowerCasePath))).Returns(lowerCaseFileSystemMetadata);
            DirectoryService directoryService = new DirectoryService(fileSystemMock.Object);

            FileSystemMetadata? lowerCaseDirResult = directoryService.GetDirectory(lowerCasePath);
            FileSystemMetadata? lowerCaseFileResult = directoryService.GetFile(lowerCasePath);
            FileSystemMetadata? upperCaseDirResult = directoryService.GetDirectory(upperCasePath);
            FileSystemMetadata? upperCaseFileResult = directoryService.GetFile(upperCasePath);

            Assert.Null(lowerCaseDirResult);
            Assert.Equal(lowerCaseFileSystemMetadata, lowerCaseFileResult);
            Assert.Null(upperCaseDirResult);
            Assert.Null(upperCaseFileResult);
        }

        [Fact]
        public void GetDirectory_GivenFilePathsWithDifferentCasing_ReturnsCorrectDirectory()
        {
            const string lowerCasePath = "/music/someartist/Lyrics";
            FileSystemMetadata lowerCaseFileSystemMetadata = new FileSystemMetadata
            {
                FullName = lowerCasePath,
                IsDirectory = true,
                Exists = true
            };
            const string upperCasePath = "/music/SOMEARTIST/LYRICS";
            FileSystemMetadata upperCaseFileSystemMetadata = new FileSystemMetadata
            {
                FullName = upperCasePath,
                IsDirectory = true,
                Exists = false
            };
            Mock<IFileSystem> fileSystemMock = new Mock<IFileSystem>();
            fileSystemMock.Setup(f => f.GetFileSystemInfo(It.Is<string>(x => x == upperCasePath))).Returns(upperCaseFileSystemMetadata);
            fileSystemMock.Setup(f => f.GetFileSystemInfo(It.Is<string>(x => x == lowerCasePath))).Returns(lowerCaseFileSystemMetadata);
            DirectoryService directoryService = new DirectoryService(fileSystemMock.Object);

            FileSystemMetadata? lowerCaseDirResult = directoryService.GetDirectory(lowerCasePath);
            FileSystemMetadata? lowerCaseFileResult = directoryService.GetFile(lowerCasePath);
            FileSystemMetadata? upperCaseDirResult = directoryService.GetDirectory(upperCasePath);
            FileSystemMetadata? upperCaseFileResult = directoryService.GetFile(upperCasePath);

            Assert.Equal(lowerCaseFileSystemMetadata, lowerCaseDirResult);
            Assert.Null(lowerCaseFileResult);
            Assert.Null(upperCaseDirResult);
            Assert.Null(upperCaseFileResult);
        }

        [Fact]
        public void GetFile_GivenCachedPath_ReturnsCachedFile()
        {
            const string path = "/music/someartist/song 1.mp3";
            FileSystemMetadata cachedFileSystemMetadata = new FileSystemMetadata
            {
                FullName = path,
                Exists = true
            };
            FileSystemMetadata newFileSystemMetadata = new FileSystemMetadata
            {
                FullName = "/music/SOMEARTIST/song 1.mp3",
                Exists = true
            };

            Mock<IFileSystem> fileSystemMock = new Mock<IFileSystem>();
            fileSystemMock.Setup(f => f.GetFileSystemInfo(It.Is<string>(x => x == path))).Returns(cachedFileSystemMetadata);
            DirectoryService directoryService = new DirectoryService(fileSystemMock.Object);

            FileSystemMetadata? result = directoryService.GetFile(path);
            fileSystemMock.Setup(f => f.GetFileSystemInfo(It.Is<string>(x => x == path))).Returns(newFileSystemMetadata);
            FileSystemMetadata? secondResult = directoryService.GetFile(path);

            Assert.Equivalent(cachedFileSystemMetadata, result);
            Assert.Equivalent(cachedFileSystemMetadata, secondResult);
        }

        [Fact]
        public void GetFilePaths_GivenCachedFilePathWithoutClear_ReturnsOnlyCachedPaths()
        {
            const string path = "/music/someartist";

            string[] cachedPaths = new[]
            {
                "/music/someartist/song 1.mp3",
                "/music/someartist/song 2.mp3",
                "/music/someartist/song 3.mp3",
                "/music/someartist/song 4.mp3",
            };
            string[] newPaths = new[]
            {
                "/music/someartist/song 5.mp3",
                "/music/someartist/song 6.mp3",
                "/music/someartist/song 7.mp3",
                "/music/someartist/song 8.mp3",
            };

            Mock<IFileSystem> fileSystemMock = new Mock<IFileSystem>();
            fileSystemMock.Setup(f => f.GetFilePaths(It.Is<string>(x => x == path), false)).Returns(cachedPaths);
            DirectoryService directoryService = new DirectoryService(fileSystemMock.Object);

            IReadOnlyList<string> result = directoryService.GetFilePaths(path);
            fileSystemMock.Setup(f => f.GetFilePaths(It.Is<string>(x => x == path), false)).Returns(newPaths);
            IReadOnlyList<string> secondResult = directoryService.GetFilePaths(path);

            Assert.Equal(cachedPaths, result);
            Assert.Equal(cachedPaths, secondResult);
        }

        [Fact]
        public void GetFilePaths_GivenCachedFilePathWithClear_ReturnsNewPaths()
        {
            const string path = "/music/someartist";

            string[] cachedPaths = new[]
            {
                "/music/someartist/song 1.mp3",
                "/music/someartist/song 2.mp3",
                "/music/someartist/song 3.mp3",
                "/music/someartist/song 4.mp3",
            };
            string[] newPaths = new[]
            {
                "/music/someartist/song 5.mp3",
                "/music/someartist/song 6.mp3",
                "/music/someartist/song 7.mp3",
                "/music/someartist/song 8.mp3",
            };

            Mock<IFileSystem> fileSystemMock = new Mock<IFileSystem>();
            fileSystemMock.Setup(f => f.GetFilePaths(It.Is<string>(x => x == path), false)).Returns(cachedPaths);
            DirectoryService directoryService = new DirectoryService(fileSystemMock.Object);

            IReadOnlyList<string> result = directoryService.GetFilePaths(path);
            fileSystemMock.Setup(f => f.GetFilePaths(It.Is<string>(x => x == path), false)).Returns(newPaths);
            IReadOnlyList<string> secondResult = directoryService.GetFilePaths(path, true);

            Assert.Equal(cachedPaths, result);
            Assert.Equal(newPaths, secondResult);
        }
    }
}
