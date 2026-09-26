using Dapper;
using Microsoft.VisualBasic.FileIO;
using Tvnoms.Proxy.Configuration;
using Tvnoms.Proxy.Data.Repositories;

namespace Tvnoms.Proxy.Data;

/// <summary>
/// Service registration extensions for the improved database layer
/// </summary>
public static class DatabaseServiceExtensions {
  /// <summary>
  /// Register all database-related services
  /// </summary>
  public static IServiceCollection AddDatabase(this IServiceCollection services) {
    // Configure Dapper type handlers for SQLite compatibility
    SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
    SqlMapper.AddTypeHandler(new GuidHandler());

    // Core database services
    services.AddSingleton<IDbConnectionFactory, SqliteConnectionFactory>();
    services.AddScoped<IUnitOfWork, UnitOfWork>();

    // Repository services
    services.AddScoped<IRecordingRepository, RecordingRepository>();
    services.AddScoped<ISettingsRepository, SettingsRepository>();

    // Generic repository for other entities if needed
    services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

    return services;
  }

  /// <summary>
  /// Get the database filename based on environment
  /// </summary>
  public static string GetConfigurationDbFileName() {
    var isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
    return isDevelopment ? "config_dev.db" : "config.db";
  }

  /// <summary>
  /// Get the configuration database file path
  /// </summary>
  public static string GetConfigurationDbFilePath() {
    var dbFile = Path.Combine(AppPaths.AppDataDirectory, GetConfigurationDbFileName());

    Directory.CreateDirectory(Path.GetDirectoryName(dbFile)!);

    return dbFile;
  }

  /// <summary>
  /// Get the configuration database connection string
  /// </summary>
  public static string GetConfigurationDbConnectionString() {
    return $"Data Source={GetConfigurationDbFilePath()}";
  }
}
