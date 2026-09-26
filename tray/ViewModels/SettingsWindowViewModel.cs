using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tvnoms.Tray.Models;
using Tvnoms.Tray.Services;

namespace Tvnoms.Tray.ViewModels;

/// <summary>Edits only the "Open web UI" target. Every other proxy setting is managed through the
/// web UI itself (which talks to the proxy directly) - the tray only needs a way to point at that
/// web UI in the first place, which is why this is the one setting the tray still edits.</summary>
public partial class SettingsWindowViewModel : ObservableObject {
  private readonly ProxyApiClient _apiClient;
  private readonly string _baseUrl;

  // The rest of the proxy's settings, fetched on load and round-tripped unchanged on save - the
  // proxy's /settings endpoint requires the full payload (MediaPlayerPath, RecordingsPath, Port
  // are all validated as present), even though this window only lets the user change WebUiUrl.
  private SettingsDto? _loadedSettings;

  [ObservableProperty] private string? _webUiUrl;
  [ObservableProperty] private bool _isLoading;
  [ObservableProperty] private string? _errorMessage;
  [ObservableProperty] private string? _statusMessage;

  public event EventHandler? CloseRequested;

  public SettingsWindowViewModel(ProxyApiClient apiClient, string baseUrl) {
    _apiClient = apiClient;
    _baseUrl = baseUrl;
    _ = LoadAsync();
  }

  private async Task LoadAsync() {
    IsLoading = true;
    ErrorMessage = null;
    try {
      var settings = await _apiClient.GetSettingsAsync(_baseUrl, CancellationToken.None);
      if (settings is null) {
        ErrorMessage = "Could not reach the proxy.";
        return;
      }

      _loadedSettings = settings;
      WebUiUrl = settings.WebUiUrl;
    } catch (Exception ex) {
      ErrorMessage = $"Could not load settings: {ex.Message}";
    } finally {
      IsLoading = false;
    }
  }

  [RelayCommand]
  private async Task SaveAsync() {
    ErrorMessage = null;
    StatusMessage = null;

    if (_loadedSettings is null) {
      ErrorMessage = "Settings haven't loaded yet.";
      return;
    }

    try {
      var dto = _loadedSettings with { WebUiUrl = WebUiUrl };
      var errors = await _apiClient.SaveSettingsAsync(_baseUrl, dto, CancellationToken.None);
      if (errors.Count > 0) {
        ErrorMessage = string.Join(" ", errors);
        return;
      }

      _loadedSettings = dto;
      StatusMessage = "Saved.";
    } catch (Exception ex) {
      ErrorMessage = $"Could not save settings: {ex.Message}";
    }
  }

  [RelayCommand]
  private void Close() {
    CloseRequested?.Invoke(this, EventArgs.Empty);
  }
}
