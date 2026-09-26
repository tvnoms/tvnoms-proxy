using Microsoft.Extensions.Options;
using Tvnoms.Proxy.Configuration;
using Tvnoms.Proxy.Data.Repositories;

namespace Tvnoms.Proxy.Services;

/// <summary>
/// The allowed length of a recording, as configured by the user.
/// </summary>
public interface IRecordingBounds {
  Task<(int Min, int Max)> GetAsync(CancellationToken cancellationToken = default);
}

public class RecordingBounds : IRecordingBounds {
  public const string MinKey = "MinDurationMinutes";
  public const string MaxKey = "MaxDurationMinutes";

  private readonly ISettingsRepository _settingsRepository;
  private readonly RecordingsConfiguration _config;
  private readonly ILogger<RecordingBounds> _logger;

  public RecordingBounds(
    ISettingsRepository settingsRepository,
    IOptions<AppConfiguration> config,
    ILogger<RecordingBounds> logger) {
    _settingsRepository = settingsRepository;
    _config = config.Value.Recordings;
    _logger = logger;
  }

  /// <summary>
  /// Reads the whole settings dictionary rather than two single-key lookups. Deliberate:
  /// <see cref="ISettingsRepository.GetSettingAsync"/> falls back to the media player arguments for
  /// any key it cannot find, so asking it for a duration would hand back an mpv command line.
  /// </summary>
  public async Task<(int Min, int Max)> GetAsync(CancellationToken cancellationToken = default) {
    var settings = await _settingsRepository.GetSettingsAsync();

    return (
      Read(settings, MinKey, _config.MinDurationMinutes),
      Read(settings, MaxKey, _config.MaxDurationMinutes));
  }

  /// <summary>
  /// A missing or unparseable stored value falls back to appsettings rather than failing the
  /// request. A bad row in the settings table must never make recording impossible.
  /// </summary>
  private int Read(IReadOnlyDictionary<string, string> settings, string key, int fallback) {
    if (!settings.TryGetValue(key, out var raw)) {
      return fallback;
    }

    if (int.TryParse(raw, out var value)) {
      return value;
    }

    _logger.LogWarning("Setting {Key} is not a number ({Value}), falling back to {Fallback}", key, raw, fallback);
    return fallback;
  }
}
