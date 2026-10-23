using System;
using System.Linq;
using Jellyfin.Database.Implementations;
using Jellyfin.Server.ServerSetupApp;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Migrations.Routines;

/// <summary>
/// Migrate rating levels.
/// </summary>
#pragma warning disable CS0618 // Type or member is obsolete
[JellyfinMigration("2025-04-20T22:00:00", nameof(MigrateRatingLevels))]
[JellyfinMigrationBackup(JellyfinDb = true)]
#pragma warning restore CS0618 // Type or member is obsolete
internal class MigrateRatingLevels : IDatabaseMigrationRoutine
{
    private readonly IStartupLogger _logger;
    private readonly IDbContextFactory<JellyfinDbContext> _provider;
    private readonly ILocalizationManager _localizationManager;

    public MigrateRatingLevels(
        IDbContextFactory<JellyfinDbContext> provider,
        IStartupLogger<MigrateRatingLevels> logger,
        ILocalizationManager localizationManager)
    {
        _provider = provider;
        _localizationManager = localizationManager;
        _logger = logger;
    }

    /// <inheritdoc/>
    public void Perform()
    {
        _logger.LogInformation("Recalculating parental rating levels based on rating string.");
        using JellyfinDbContext context = _provider.CreateDbContext();
        using IDbContextTransaction transaction = context.Database.BeginTransaction();
        IQueryable<string?> ratings = context.BaseItems.AsNoTracking().Select(e => e.OfficialRating).Distinct();
        foreach (string? rating in ratings)
        {
            if (string.IsNullOrEmpty(rating))
            {
                int? value = null;
                context.BaseItems
                    .Where(e => e.OfficialRating == null || e.OfficialRating == string.Empty)
                    .ExecuteUpdate(f => f.SetProperty(e => e.InheritedParentalRatingValue, value));
                context.BaseItems
                    .Where(e => e.OfficialRating == null || e.OfficialRating == string.Empty)
                    .ExecuteUpdate(f => f.SetProperty(e => e.InheritedParentalRatingSubValue, value));
            }
            else
            {
                ParentalRatingScore? ratingValue = _localizationManager.GetRatingScore(rating);
                int? score = ratingValue?.Score;
                int? subScore = ratingValue?.SubScore;
                context.BaseItems
                    .Where(e => e.OfficialRating == rating)
                    .ExecuteUpdate(f => f.SetProperty(e => e.InheritedParentalRatingValue, score));
                context.BaseItems
                    .Where(e => e.OfficialRating == rating)
                    .ExecuteUpdate(f => f.SetProperty(e => e.InheritedParentalRatingSubValue, subScore));
            }
        }

        transaction.Commit();
    }
}
