using Emby.Server.Implementations.Playlists;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Playlists;

public class PlaylistManagerTests
{
    [Fact]
    public void DetermineAdjustedIndexMoveToFirstPositionNoPriorInAllList()
    {
        int priorIndexAllChildren = 0;
        int newIndex = 0;

        int adjustedIndex = PlaylistManager.DetermineAdjustedIndex(priorIndexAllChildren, newIndex);

        Assert.Equal(0, adjustedIndex);
    }

    [Fact]
    public void DetermineAdjustedIndexPriorInMiddleOfAllList()
    {
        int priorIndexAllChildren = 2;
        int newIndex = 0;

        int adjustedIndex = PlaylistManager.DetermineAdjustedIndex(priorIndexAllChildren, newIndex);

        Assert.Equal(1, adjustedIndex);
    }

    [Fact]
    public void DetermineAdjustedIndexMoveMiddleOfPlaylist()
    {
        int priorIndexAllChildren = 2;
        int newIndex = 1;

        int adjustedIndex = PlaylistManager.DetermineAdjustedIndex(priorIndexAllChildren, newIndex);

        Assert.Equal(3, adjustedIndex);
    }
}
