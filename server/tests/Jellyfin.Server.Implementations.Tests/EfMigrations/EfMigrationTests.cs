using Jellyfin.Database.Implementations;
using Jellyfin.Database.Providers.Sqlite.Migrations;
using Jellyfin.Server.Implementations.Migrations;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.EfMigrations;

public class EfMigrationTests
{
    [Fact]
    public void CheckForUnappliedMigrations_SqLite()
    {
        SqliteDesignTimeJellyfinDbFactory dbDesignContext = new SqliteDesignTimeJellyfinDbFactory();
        JellyfinDbContext context = dbDesignContext.CreateDbContext([]);
        Assert.False(context.Database.HasPendingModelChanges(), "There are unapplied changes to the EFCore model for SQLite. Please create a Migration.");
    }
}
