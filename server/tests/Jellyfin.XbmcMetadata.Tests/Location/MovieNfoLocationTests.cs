using System;
using System.Linq;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.XbmcMetadata.Savers;
using Xunit;

namespace Jellyfin.XbmcMetadata.Tests.Location
{
    public class MovieNfoLocationTests
    {
        [Fact]
        public static void Movie_MixedFolder_Success()
        {
            Movie movie = new Movie() { Path = "/media/movies/Avengers Endgame.mp4", IsInMixedFolder = true };

            string[] paths = MovieNfoSaver.GetMovieSavePaths(new ItemInfo(movie)).ToArray();
            Assert.Single(paths);
            Assert.Contains("/media/movies/Avengers Endgame.nfo", paths);
        }

        [Fact]
        public static void Movie_SeparateFolder_Success()
        {
            Movie movie = new Movie() { Path = "/media/movies/Avengers Endgame/Avengers Endgame.mp4" };
            string path1 = "/media/movies/Avengers Endgame/Avengers Endgame.nfo";
            string path2 = "/media/movies/Avengers Endgame/movie.nfo";

            // uses ContainingFolderPath which uses Operating system specific paths
            if (OperatingSystem.IsWindows())
            {
                movie.Path = movie.Path.Replace('/', '\\');
                path1 = path1.Replace('/', '\\');
                path2 = path2.Replace('/', '\\');
            }

            string[] paths = MovieNfoSaver.GetMovieSavePaths(new ItemInfo(movie)).ToArray();
            Assert.Equal(2, paths.Length);
            Assert.Contains(path1, paths);
            Assert.Contains(path2, paths);
        }

        [Fact]
        public void Movie_DVD_Success()
        {
            Movie movie = new Movie() { Path = "/media/movies/Avengers Endgame", VideoType = VideoType.Dvd };
            string path1 = "/media/movies/Avengers Endgame/Avengers Endgame.nfo";
            string path2 = "/media/movies/Avengers Endgame/VIDEO_TS/VIDEO_TS.nfo";
            string path3 = "/media/movies/Avengers Endgame/movie.nfo";

            // uses ContainingFolderPath which uses Operating system specific paths
            if (OperatingSystem.IsWindows())
            {
                movie.Path = movie.Path.Replace('/', '\\');
                path1 = path1.Replace('/', '\\');
                path2 = path2.Replace('/', '\\');
                path3 = path3.Replace('/', '\\');
            }

            string[] paths = MovieNfoSaver.GetMovieSavePaths(new ItemInfo(movie)).ToArray();
            Assert.Equal(3, paths.Length);
            Assert.Contains(path1, paths);
            Assert.Contains(path2, paths);
            Assert.Contains(path3, paths);
        }
    }
}
