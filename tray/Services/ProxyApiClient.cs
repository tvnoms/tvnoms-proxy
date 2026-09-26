using System.Net.Http.Json;
using System.Text.Json;
using Tvnoms.Tray.Models;

namespace Tvnoms.Tray.Services;

/// <summary>Thin REST client for the one endpoint the tray needs: a startup/reconnect snapshot
/// of what's currently recording. tvnoms-proxy has no status filter on this route, so it
/// returns everything and we filter client-side.</summary>
public class ProxyApiClient(HttpClient httpClient) {
  private static readonly JsonSerializerOptions JsonOptions = new() {
    PropertyNameCaseInsensitive = true,
  };

  public async Task<IReadOnlyList<RecordingDto>> GetActiveRecordingsAsync(string baseUrl, CancellationToken ct) {
    var recordings = await httpClient.GetFromJsonAsync<List<RecordingDto>>(
      $"{baseUrl}/recordings", JsonOptions, ct);
    return recordings?.Where(r => r.Status == "recording").ToList() ?? [];
  }

  public async Task<SettingsDto?> GetSettingsAsync(string baseUrl, CancellationToken ct) =>
    await httpClient.GetFromJsonAsync<SettingsDto>($"{baseUrl}/settings", JsonOptions, ct);

  /// <summary>Posts updated settings. Returns the empty list on success, or a list of
  /// human-readable error messages (from FluentValidation's 400 body, or a plain status-code
  /// message for anything else) on failure - never throws for an ordinary rejected save.</summary>
  public async Task<IReadOnlyList<string>> SaveSettingsAsync(string baseUrl, SettingsDto dto, CancellationToken ct) {
    var response = await httpClient.PostAsJsonAsync($"{baseUrl}/settings", dto, JsonOptions, ct);
    if (response.IsSuccessStatusCode) {
      return [];
    }

    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest) {
      var errors = await response.Content.ReadFromJsonAsync<List<ValidationErrorDto>>(JsonOptions, ct);
      return errors?.Select(e => e.ErrorMessage).ToList() ?? ["Save failed: invalid settings."];
    }

    return [$"Save failed: {(int)response.StatusCode} {response.ReasonPhrase}"];
  }
}
