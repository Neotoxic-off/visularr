using Emby.Naming.AudioBook;
using Xunit;

namespace Jellyfin.Naming.Tests.AudioBook
{
    public class AudioBookFileInfoTests
    {
        [Fact]
        public void CompareTo_Same_Success()
        {
            AudioBookFileInfo info = new AudioBookFileInfo(string.Empty, string.Empty);
            Assert.Equal(0, info.CompareTo(info));
        }

        [Fact]
        public void CompareTo_Null_Success()
        {
            AudioBookFileInfo info = new AudioBookFileInfo(string.Empty, string.Empty);
            Assert.Equal(1, info.CompareTo(null));
        }

        [Fact]
        public void CompareTo_Empty_Success()
        {
            AudioBookFileInfo info1 = new AudioBookFileInfo(string.Empty, string.Empty);
            AudioBookFileInfo info2 = new AudioBookFileInfo(string.Empty, string.Empty);
            Assert.Equal(0, info1.CompareTo(info2));
        }
    }
}
