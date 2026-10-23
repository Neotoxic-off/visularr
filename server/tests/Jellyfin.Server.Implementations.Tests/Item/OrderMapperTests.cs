using System;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Server.Implementations.Item;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Item;

public class OrderMapperTests
{
    [Fact]
    public void ShouldReturnMappedOrderForSortingByPremierDate()
    {
        Func<BaseItemEntity, object?> orderFunc = OrderMapper.MapOrderByField(ItemSortBy.PremiereDate, new InternalItemsQuery(), null!).Compile();

        DateTime expectedDate = new DateTime(1, 2, 3);
        DateTime expectedProductionYearDate = new DateTime(4, 1, 1);

        BaseItemEntity entityWithOnlyProductionYear = new BaseItemEntity { Id = Guid.NewGuid(), Type = "Test", ProductionYear = expectedProductionYearDate.Year };
        BaseItemEntity entityWithOnlyPremierDate = new BaseItemEntity { Id = Guid.NewGuid(), Type = "Test", PremiereDate = expectedDate };
        BaseItemEntity entityWithBothPremierDateAndProductionYear = new BaseItemEntity { Id = Guid.NewGuid(), Type = "Test", PremiereDate = expectedDate, ProductionYear = expectedProductionYearDate.Year };
        BaseItemEntity entityWithoutEitherPremierDateOrProductionYear = new BaseItemEntity { Id = Guid.NewGuid(), Type = "Test" };

        object? resultWithOnlyProductionYear = orderFunc(entityWithOnlyProductionYear);
        object? resultWithOnlyPremierDate = orderFunc(entityWithOnlyPremierDate);
        object? resultWithBothPremierDateAndProductionYear = orderFunc(entityWithBothPremierDateAndProductionYear);
        object? resultWithoutEitherPremierDateOrProductionYear = orderFunc(entityWithoutEitherPremierDateOrProductionYear);

        Assert.Equal(resultWithOnlyProductionYear, expectedProductionYearDate);
        Assert.Equal(resultWithOnlyPremierDate, expectedDate);
        Assert.Equal(resultWithBothPremierDateAndProductionYear, expectedDate);
        Assert.Null(resultWithoutEitherPremierDateOrProductionYear);
    }
}
