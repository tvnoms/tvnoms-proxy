using Dapper;
using Microsoft.Extensions.Options;
using Tvnoms.Proxy.Configuration;
using Tvnoms.Proxy.Data.Models;

namespace Tvnoms.Proxy.Data.Repositories;

public interface ISettingsRepository : IRepository<Setting> {
  Task<Dictionary<string, string>> GetSettingsAsync();

  Task<string> GetSettingAsync(string key);

  Task UpdateOrCreateSettingAsync(string key, string value);

  Task UpdateOrCreateSettingsAsync(Dictionary<string, string> settings);
}

public class SettingsRepository : Repository<Setting>, ISettingsRepository {
  private readonly AppConfiguration _config;

  public SettingsRepository(IDbConnectionFactory connectionFactory, IOptions<AppConfiguration> config) : base(
    connectionFactory) {
    _config = config.Value;
  }

  public override async Task<Setting?> GetByIdAsync(Guid id) {
    using var connection = await _connectionFactory.CreateConnectionAsync();
    const string sql = "SELECT * FROM settings WHERE Id = @Id";
    return await connection.QueryFirstOrDefaultAsync<Setting>(sql, new {Id = id});
  }

  public override async Task<bool> DeleteAsync(Guid id) {
    using var connection = await _connectionFactory.CreateConnectionAsync();
    const string sql = "DELETE FROM settings WHERE Id = @Id";
    var result = await connection.ExecuteAsync(sql, new {Id = id});
    return result > 0;
  }

  public async Task<Dictionary<string, string>> GetSettingsAsync() {
    var settings = await GetAllAsync();
    var settingsDict = settings.ToDictionary(s => s.Key, s => s.Value);

    // Ensure default settings exist
    if (!settingsDict.ContainsKey("MediaPlayerPath")) {
      await UpdateOrCreateSettingAsync("MediaPlayerPath", _config.VideoPlayer.MediaPlayerPath);
      settingsDict["MediaPlayerPath"] = _config.VideoPlayer.MediaPlayerPath;
    }

    if (!settingsDict.ContainsKey("MediaPlayerArguments")) {
      await UpdateOrCreateSettingAsync("MediaPlayerArguments", _config.VideoPlayer.MediaPlayerArguments);
      settingsDict["MediaPlayerArguments"] = _config.VideoPlayer.MediaPlayerArguments;
    }

    if (!settingsDict.ContainsKey("RecordingsPath")) {
      var defaultPath = _config.Recordings.Path ??
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Recordings");
      await UpdateOrCreateSettingAsync("RecordingsPath", defaultPath);
      settingsDict["RecordingsPath"] = defaultPath;
    }

    // Seeded, never force-synced: unlike Port below, these are meant to be changed from the
    // settings page and to survive whatever appsettings.json happens to say afterwards.
    if (!settingsDict.ContainsKey("MinDurationMinutes")) {
      var min = _config.Recordings.MinDurationMinutes.ToString();
      await UpdateOrCreateSettingAsync("MinDurationMinutes", min);
      settingsDict["MinDurationMinutes"] = min;
    }

    if (!settingsDict.ContainsKey("MaxDurationMinutes")) {
      var max = _config.Recordings.MaxDurationMinutes.ToString();
      await UpdateOrCreateSettingAsync("MaxDurationMinutes", max);
      settingsDict["MaxDurationMinutes"] = max;
    }

    // Defensive fallback for installs that somehow reach here without the AddWebUiUrlSetting
    // migration having run. _config.WebUiUrl has no compiled-in default (Program.cs computes it),
    // so guard against writing an empty value that would permanently disable the tray's button.
    if (!settingsDict.ContainsKey("WebUiUrl") && !string.IsNullOrEmpty(_config.WebUiUrl)) {
      await UpdateOrCreateSettingAsync("WebUiUrl", _config.WebUiUrl);
      settingsDict["WebUiUrl"] = _config.WebUiUrl;
    }

    // Always sync Port from appsettings configuration on startup
    var configuredPort = _config.Networking.Port.ToString();
    if (!settingsDict.ContainsKey("Port")) {
      await UpdateOrCreateSettingAsync("Port", configuredPort);
      settingsDict["Port"] = configuredPort;
    } else if (settingsDict["Port"] != configuredPort) {
      // Update database port to match appsettings configuration
      await UpdateOrCreateSettingAsync("Port", configuredPort);
      settingsDict["Port"] = configuredPort;
    }

    return settingsDict;
  }

  public async Task<string> GetSettingAsync(string key) {
    var settings = await GetAllAsync();
    return settings
             .FirstOrDefault(s => s.Key == key)?.Value ??
           _config.VideoPlayer.MediaPlayerArguments;
  }

  public async Task UpdateOrCreateSettingAsync(string key, string value) {
    var existing = (await GetAllAsync()).FirstOrDefault(s => s.Key == key);

    using var connection = await _connectionFactory.CreateConnectionAsync();

    if (existing == null) {
      const string insertSql = @"INSERT INTO settings (Id, Key, Value) VALUES (@Id, @Key, @Value)";
      var insertResult = await connection.ExecuteAsync(insertSql, new {Id = Guid.NewGuid(), Key = key, Value = value});
      if (insertResult <= 0) {
        throw new InvalidOperationException($"Failed to insert setting '{key}'. Insert returned {insertResult}");
      }
    } else {
      const string updateSql = @"UPDATE settings SET Value = @Value WHERE Key = @Key";
      var updateResult = await connection.ExecuteAsync(updateSql, new {Value = value, Key = key});
      if (updateResult <= 0) {
        throw new InvalidOperationException($"Failed to update setting '{key}'. Update returned {updateResult}");
      }
    }
  }

  public async Task UpdateOrCreateSettingsAsync(Dictionary<string, string> settings) {
    foreach (var (key, value) in settings) {
      await UpdateOrCreateSettingAsync(key, value);
    }
  }
}
