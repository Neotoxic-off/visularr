using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.DbConfiguration;
using MediaBrowser.Common.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Database.Providers.Sqlite;

/// <summary>
/// Configures jellyfin to use an SQLite database.
/// </summary>
[JellyfinDatabaseProviderKey("Jellyfin-SQLite")]
public sealed class SqliteDatabaseProvider : IJellyfinDatabaseProvider
{
    private const string BackupFolderName = "SQLiteBackups";
    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<SqliteDatabaseProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteDatabaseProvider"/> class.
    /// </summary>
    /// <param name="applicationPaths">Service to construct the fallback when the old data path configuration is used.</param>
    /// <param name="logger">A logger.</param>
    public SqliteDatabaseProvider(IApplicationPaths applicationPaths, ILogger<SqliteDatabaseProvider> logger)
    {
        _applicationPaths = applicationPaths;
        _logger = logger;
    }

    /// <inheritdoc/>
    public IDbContextFactory<JellyfinDbContext>? DbContextFactory { get; set; }

    /// <inheritdoc/>
    public void Initialise(DbContextOptionsBuilder options, DatabaseConfigurationOptions databaseConfiguration)
    {
        static T? GetOption<T>(ICollection<CustomDatabaseOption>? options, string key, Func<string, T> converter, Func<T>? defaultValue = null)
        {
            if (options is null)
            {
                return defaultValue is not null ? defaultValue() : default;
            }

            CustomDatabaseOption? value = options.FirstOrDefault(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (value is null)
            {
                return defaultValue is not null ? defaultValue() : default;
            }

            return converter(value.Value);
        }

        Collection<CustomDatabaseOption>? customOptions = databaseConfiguration.CustomProviderOptions?.Options;

        SqliteConnectionStringBuilder sqliteConnectionBuilder = new SqliteConnectionStringBuilder();
        sqliteConnectionBuilder.DataSource = Path.Combine(_applicationPaths.DataPath, "jellyfin.db");
        sqliteConnectionBuilder.Cache = GetOption(customOptions, "cache", Enum.Parse<SqliteCacheMode>, () => SqliteCacheMode.Default);
        sqliteConnectionBuilder.Pooling = GetOption(customOptions, "pooling", e => e.Equals(bool.TrueString, StringComparison.OrdinalIgnoreCase), () => true);
        sqliteConnectionBuilder.DefaultTimeout = GetOption(customOptions, "command-timeout", int.Parse, () => 30);

        string connectionString = sqliteConnectionBuilder.ToString();

        Dictionary<string, string> customPragma = customOptions?.Where(e => e.Key.StartsWith("#PRAGMA:", StringComparison.OrdinalIgnoreCase)).ToDictionary(e => e.Key["#PRAGMA:".Length..], e => e.Value) ?? [];
        // visularr: memory map the first 256 MB of the database unless database.xml sets #PRAGMA:mmap_size.
        if (!customPragma.Keys.Any(k => k.Equals("mmap_size", StringComparison.OrdinalIgnoreCase)))
        {
            customPragma["mmap_size"] = "268435456";
        }

        // Log SQLite connection parameters
        _logger.LogInformation("SQLite connection string: {ConnectionString}", connectionString);

        options
            .UseSqlite(
                connectionString,
                sqLiteOptions => sqLiteOptions.MigrationsAssembly(GetType().Assembly))
            // TODO: Remove when https://github.com/dotnet/efcore/pull/35873 is merged & released
            .ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.NonTransactionalMigrationOperationWarning))
            .AddInterceptors(new PragmaConnectionInterceptor(
                _logger,
                // visularr: a 64 MB page cache (negative is KiB) unless database.xml sets cacheSize.
                GetOption<int?>(customOptions, "cacheSize", e => int.Parse(e, CultureInfo.InvariantCulture), () => -65536),
                GetOption(customOptions, "lockingmode", e => e, () => "NORMAL")!,
                GetOption(customOptions, "journalsizelimit", int.Parse, () => 134_217_728),
                GetOption(customOptions, "tempstoremode", int.Parse, () => 2),
                GetOption(customOptions, "syncmode", int.Parse, () => 1),
                customPragma));

        bool enableSensitiveDataLogging = GetOption(customOptions, "EnableSensitiveDataLogging", e => e.Equals(bool.TrueString, StringComparison.OrdinalIgnoreCase), () => false);
        if (enableSensitiveDataLogging)
        {
            options.EnableSensitiveDataLogging(enableSensitiveDataLogging);
            _logger.LogInformation("EnableSensitiveDataLogging is enabled on SQLite connection");
        }
    }

    /// <inheritdoc/>
    public async Task RunScheduledOptimisation(CancellationToken cancellationToken)
    {
        JellyfinDbContext context = await DbContextFactory!.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE)", cancellationToken).ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync("PRAGMA optimize", cancellationToken).ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync("VACUUM", cancellationToken).ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE)", cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("jellyfin.db optimized successfully!");
        }
    }

    /// <inheritdoc/>
    public void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.SetDefaultDateTimeKind(DateTimeKind.Utc);
    }

    /// <inheritdoc/>
    public async Task RunShutdownTask(CancellationToken cancellationToken)
    {
        if (DbContextFactory is null)
        {
            return;
        }

        // Run before disposing the application
        JellyfinDbContext context = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA optimize", cancellationToken).ConfigureAwait(false);
        }

        SqliteConnection.ClearAllPools();
    }

    /// <inheritdoc/>
    public void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Conventions.Add(_ => new DoNotUseReturningClauseConvention());
    }

    /// <inheritdoc />
    public Task<string> MigrationBackupFast(CancellationToken cancellationToken)
    {
        string key = DateTime.UtcNow.ToString("yyyyMMddhhmmss", CultureInfo.InvariantCulture);
        string path = Path.Combine(_applicationPaths.DataPath, "jellyfin.db");
        string backupFile = Path.Combine(_applicationPaths.DataPath, BackupFolderName);
        Directory.CreateDirectory(backupFile);

        backupFile = Path.Combine(backupFile, $"{key}_jellyfin.db");
        File.Copy(path, backupFile);
        return Task.FromResult(key);
    }

    /// <inheritdoc />
    public Task RestoreBackupFast(string key, CancellationToken cancellationToken)
    {
        // ensure there are absolutly no dangling Sqlite connections.
        SqliteConnection.ClearAllPools();
        string path = Path.Combine(_applicationPaths.DataPath, "jellyfin.db");
        string backupFile = Path.Combine(_applicationPaths.DataPath, BackupFolderName, $"{key}_jellyfin.db");

        if (!File.Exists(backupFile))
        {
            _logger.LogCritical("Tried to restore a backup that does not exist: {Key}", key);
            return Task.CompletedTask;
        }

        File.Copy(backupFile, path, true);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteBackup(string key)
    {
        string backupFile = Path.Combine(_applicationPaths.DataPath, BackupFolderName, $"{key}_jellyfin.db");

        if (!File.Exists(backupFile))
        {
            _logger.LogCritical("Tried to delete a backup that does not exist: {Key}", key);
            return Task.CompletedTask;
        }

        File.Delete(backupFile);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task PurgeDatabase(JellyfinDbContext dbContext, IEnumerable<string>? tableNames)
    {
        ArgumentNullException.ThrowIfNull(tableNames);

        List<string> deleteQueries = new List<string>();
        foreach (string tableName in tableNames)
        {
            deleteQueries.Add($"DELETE FROM \"{tableName}\";");
        }

        string deleteAllQuery =
        $"""
        PRAGMA foreign_keys = OFF;
        {string.Join('\n', deleteQueries)}
        PRAGMA foreign_keys = ON;
        """;

        await dbContext.Database.ExecuteSqlRawAsync(deleteAllQuery).ConfigureAwait(false);
    }
}
