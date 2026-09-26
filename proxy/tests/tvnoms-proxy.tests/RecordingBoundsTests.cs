using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Tvnoms.Proxy.Configuration;
using Tvnoms.Proxy.Data.Repositories;
using Tvnoms.Proxy.Services;

namespace Tvnoms.Proxy.Tests;

public class RecordingBoundsTests {
  /// <summary>
  /// The point of the change: appsettings seeds these once, but what the user saved is what
  /// applies from then on.
  /// </summary>
  [Fact]
  public async Task StoredSettings_WinOverAppSettings() {
    var bounds = Bounds(
      stored: new() { ["MinDurationMinutes"] = "15", ["MaxDurationMinutes"] = "120" },
      configMin: 1, configMax: 600);

    var (min, max) = await bounds.GetAsync();

    Assert.Equal(15, min);
    Assert.Equal(120, max);
  }

  [Fact]
  public async Task MissingSettings_FallBackToAppSettings() {
    var bounds = Bounds(stored: new(), configMin: 3, configMax: 420);

    var (min, max) = await bounds.GetAsync();

    Assert.Equal(3, min);
    Assert.Equal(420, max);
  }

  /// <summary>
  /// A junk row must not make recording impossible, so it degrades to the configured default
  /// rather than throwing.
  /// </summary>
  [Fact]
  public async Task UnparseableSetting_FallsBackToAppSettings() {
    var bounds = Bounds(
      stored: new() { ["MinDurationMinutes"] = "not-a-number", ["MaxDurationMinutes"] = "120" },
      configMin: 1, configMax: 600);

    var (min, max) = await bounds.GetAsync();

    Assert.Equal(1, min);
    Assert.Equal(120, max);
  }

  private static RecordingBounds Bounds(Dictionary<string, string> stored, int configMin, int configMax) {
    var config = Options.Create(new AppConfiguration {
      Recordings = new RecordingsConfiguration {
        MinDurationMinutes = configMin,
        MaxDurationMinutes = configMax
      }
    });

    return new RecordingBounds(
      new FakeSettingsRepository(stored),
      config,
      NullLogger<RecordingBounds>.Instance);
  }
}
