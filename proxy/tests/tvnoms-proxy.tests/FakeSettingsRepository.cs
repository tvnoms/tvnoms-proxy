using Tvnoms.Proxy.Data.Models;
using Tvnoms.Proxy.Data.Repositories;

namespace Tvnoms.Proxy.Tests;

/// <summary>
/// In-memory <see cref="ISettingsRepository"/> backed by a plain dictionary. Note it does not
/// reproduce the real repository's default-seeding or its odd single-key fallback - a test that
/// cares about those should say so explicitly rather than inherit them here.
/// </summary>
internal sealed class FakeSettingsRepository : ISettingsRepository {
  private readonly Dictionary<string, string> _settings;

  public FakeSettingsRepository(Dictionary<string, string> settings) {
    _settings = settings;
  }

  public Task<Dictionary<string, string>> GetSettingsAsync() {
    return Task.FromResult(new Dictionary<string, string>(_settings));
  }

  public Task<string> GetSettingAsync(string key) {
    return Task.FromResult(_settings.TryGetValue(key, out var value) ? value : string.Empty);
  }

  public Task UpdateOrCreateSettingAsync(string key, string value) {
    _settings[key] = value;
    return Task.CompletedTask;
  }

  public Task UpdateOrCreateSettingsAsync(Dictionary<string, string> settings) {
    foreach (var (key, value) in settings) {
      _settings[key] = value;
    }

    return Task.CompletedTask;
  }

  public Task<IEnumerable<Setting>> GetAllAsync() {
    var values = _settings.Select(kv => new Setting {
      Id = Guid.NewGuid(),
      Key = kv.Key,
      Value = kv.Value
    });
    return Task.FromResult(values);
  }

  public Task<Setting?> GetByIdAsync(Guid id) {
    return Task.FromResult<Setting?>(null);
  }

  public Task<int> InsertAsync(Setting entity) {
    throw new NotSupportedException();
  }

  public Task<bool> UpdateAsync(Setting entity) {
    throw new NotSupportedException();
  }

  public Task<bool> DeleteAsync(Guid id) {
    throw new NotSupportedException();
  }
}
