namespace Tvnoms.Proxy.Services;

/// <summary>
/// Helpers for reasoning about recording files on disk.
/// </summary>
public static class RecordingFiles {
  /// <summary>
  /// True when the path points at a file that actually holds video. Recordings are written as
  /// fragmented MP4, so a file left behind by an interrupted capture is still playable - but a
  /// capture that fell over before ffmpeg wrote anything leaves a zero-byte stub that is not.
  /// </summary>
  public static bool HasUsableVideo(string? path) {
    if (string.IsNullOrEmpty(path)) {
      return false;
    }

    var file = new FileInfo(path);
    return file.Exists && file.Length > 0;
  }
}
