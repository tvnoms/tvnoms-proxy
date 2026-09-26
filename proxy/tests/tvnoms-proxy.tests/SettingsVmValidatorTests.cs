using Xunit;
using Tvnoms.Proxy.Models;

namespace Tvnoms.Proxy.Tests;

public class SettingsVmValidatorTests {
  private static SettingsVm Settings(string? webUiUrl) => new() {
    MediaPlayerPath = "/usr/bin/mpv",
    MediaPlayerArguments = "",
    RecordingsPath = "/home/user/recordings",
    Port = 8963,
    WebUiUrl = webUiUrl
  };

  [Fact]
  public async Task ValidAbsoluteUrl_IsValid() {
    var validator = new SettingsVmValidator();

    var result = await validator.ValidateAsync(Settings("https://streams.ferg.al"));

    Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(e => e.ErrorMessage)));
  }

  [Fact]
  public async Task NonUrlString_IsInvalid() {
    var validator = new SettingsVmValidator();

    var result = await validator.ValidateAsync(Settings("not-a-url"));

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.ErrorMessage == "Web UI URL must be a valid absolute URL.");
  }

  [Fact]
  public async Task OmittedWebUiUrl_IsValid() {
    var validator = new SettingsVmValidator();

    var result = await validator.ValidateAsync(Settings(null));

    Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(e => e.ErrorMessage)));
  }
}
