namespace Tvnoms.Proxy.Services;

/// <summary>
/// Interface for video recording operations
/// </summary>
public interface IRecordingService {
  /// <param name="onOutputFileCreated">
  /// Invoked with the output path as soon as it is known, before ffmpeg writes a single frame, so
  /// the caller can persist it. Whatever happens next - a clean finish, an error, or the process
  /// being killed outright - the file can still be found again.
  /// </param>
  /// <param name="onProgress">
  /// Invoked with the media time ffmpeg has actually captured, throttled to at most one call a
  /// second. The value is absolute rather than incremental, so a consumer that drops one loses
  /// nothing. Note this stops being called entirely when the input stalls - that silence is the
  /// only signal there is that a stream has died, so do not paper over it with a heartbeat.
  /// </param>
  Task<string> RecordShow(string url, DateTimeOffset startTime, DateTimeOffset endTime,
    Func<string, Task>? onOutputFileCreated = null, Action<TimeSpan>? onProgress = null,
    CancellationToken cancellationToken = default);

  Task<bool> DeleteRecordingAsync(Guid recordingId, CancellationToken cancellationToken = default);

  /// <summary>
  /// Resolves rows left mid-capture by a previous run, keeping whatever video reached disk.
  /// Returns the number of recordings reconciled.
  /// </summary>
  Task<int> ReconcileInterruptedRecordingsAsync();
}

