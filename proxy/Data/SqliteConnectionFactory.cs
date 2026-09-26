using System.Data;
using Microsoft.Data.Sqlite;
using Tvnoms.Proxy.Configuration;

namespace Tvnoms.Proxy.Data;

/// <summary>
/// Simplified database connection factory and configuration
/// </summary>
public interface IDbConnectionFactory {
  Task<IDbConnection> CreateConnectionAsync();
  string ConnectionString { get; }
}

public class SqliteConnectionFactory : IDbConnectionFactory {
  private readonly string _connectionString;

  public SqliteConnectionFactory() {
    var dbPath = AppPaths.AppDataDirectory;

    if (!Directory.Exists(dbPath)) {
      Directory.CreateDirectory(dbPath);
    }

    var dbFileName = DatabaseServiceExtensions.GetConfigurationDbFileName();
    var builder = new SqliteConnectionStringBuilder {
      DataSource = Path.Combine(dbPath, dbFileName),
      Cache = SqliteCacheMode.Shared,
      Mode = SqliteOpenMode.ReadWriteCreate
    };

    _connectionString = builder.ConnectionString;
  }

  public string ConnectionString => _connectionString;

  public async Task<IDbConnection> CreateConnectionAsync() {
    var connection = new SqliteConnection(_connectionString);
    await connection.OpenAsync();
    
    // Ensure reliable writes to SQLite
    using var cmd = connection.CreateCommand();
    cmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = ON;";
    cmd.ExecuteNonQuery();
    
    return connection;
  }
}
