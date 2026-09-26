namespace Tvnoms.Proxy.Services;

/// <summary>
/// Represents the outcome of a video player operation, with an optional failure reason.
/// </summary>
public sealed record PlayerResult(bool Success, string? ErrorMessage = null, bool IsClientError = false) {
  public static PlayerResult Ok() => new(true);
  public static PlayerResult ClientFail(string errorMessage) => new(false, errorMessage, IsClientError: true);
  public static PlayerResult ServerFail(string errorMessage) => new(false, errorMessage, IsClientError: false);
}

/// <summary>
/// Interface for video player operations
/// </summary>
public interface IVideoPlayerService {
  Task<PlayerResult> PlayFromUrl(string url, CancellationToken cancellationToken = default);
  Task<PlayerResult> OpenRecordingsFolderAsync(CancellationToken cancellationToken = default);
}

