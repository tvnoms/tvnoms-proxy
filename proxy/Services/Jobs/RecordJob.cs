using System.Text.Json;
using Quartz;
using Tvnoms.Proxy.Data.Models;
using Tvnoms.Proxy.Data.Repositories;
using Tvnoms.Proxy.Models;

namespace Tvnoms.Proxy.Services.Jobs;

public class RecordJob : IJob {
  private readonly IRecordingService _recorder;
  private readonly IRecordingRepository _recordingRepository;
  private readonly IRecordingNotifier _notifier;
  private readonly ILogger<RecordJob> _logger;

  public RecordJob(
    IRecordingService recorder,
    IRecordingRepository recordingRepository,
    IRecordingNotifier notifier,
    ILogger<RecordJob> logger) {
    _recorder = recorder;
    _recordingRepository = recordingRepository;
    _notifier = notifier;
    _logger = logger;
  }

  public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) {
    var jobId = context.JobDetail.Key.Name;
    if (context.Trigger.JobDataMap["data"] is not string) {
      _logger.LogError("Invalid job data {JobData}", context.Trigger.JobDataMap);
      return;
    }

    _logger.LogDebug("Recording job triggered with data {Data}",
      context.Trigger.JobDataMap["data"]);


    var requestData = context.Trigger.JobDataMap["data"].ToString();

    if (string.IsNullOrEmpty(requestData)) return;

    try {
      var data = JsonSerializer.Deserialize<RecordVm>(requestData);

      if (string.IsNullOrEmpty(data?.Url) || !data.Url.IsValidUrl()) {
        throw new InvalidOperationException("Invalid recording data");
      }

      var jobDuration = data.EndTime.Subtract(data.StartTime).TotalMinutes;
      _logger.LogTrace(
        "[DIAG] RecordJob firing — JobId: {JobId}, StartTime: {StartTime}, EndTime: {EndTime}, Duration: {DurationMinutes}min, UtcNow: {UtcNow}",
        jobId, data.StartTime, data.EndTime, Math.Round(jobDuration, 2), DateTimeOffset.UtcNow);

      var recording = await MarkStatus(jobId, "recording");

      Action<TimeSpan>? onProgress = null;
      if (recording is not null) {
        var duration = data.EndTime.Subtract(data.StartTime);
        var captureStartedAt = DateTimeOffset.UtcNow;
        onProgress = captured => {
          // Fire and forget: the notifier swallows its own failures, so nothing is left
          // unobserved, and ffmpeg's output loop must not block on a socket write.
          _ = _notifier.RecordingProgressAsync(
            recording, captured, DateTimeOffset.UtcNow.Subtract(captureStartedAt), duration);
        };
      }

       var outputFile = await _recorder.RecordShow(
         data.Url.DecodeUrl(),
         data.StartTime,
         data.EndTime,
         onOutputFileCreated: path => SetOutputFile(jobId, path),
         onProgress: onProgress,
         cancellationToken: cancellationToken);

      if (!string.IsNullOrEmpty(outputFile)) {
        // Re-read rather than reusing the row from above: SetOutputFile has written to it since.
        var completed = await _recordingRepository.GetByJobIdAsync(jobId);
        if (completed is null) {
          _logger.LogError("Failed to find recording with JobId {JobId}", jobId);
          return;
        }

        completed.IsRecorded = true;
        completed.FilePath = outputFile;
        completed.Status = "complete"; // Successfully recorded
        await SaveAndAnnounce(completed);
      }
    } catch (OperationCanceledException) {
      _logger.LogInformation("Recording job {JobId} was cancelled", jobId);
      await FinaliseInterrupted(jobId);
    } catch (JsonException jse) {
      // Nothing was ever scheduled to disk, so there is no file to salvage
      _logger.LogError(jse, "Failed to deserialize recording data");
      await MarkStatus(jobId, "failed");
    } catch (Exception ex) {
      _logger.LogError(ex, "Recording job {JobId} failed", jobId);
      await FinaliseInterrupted(jobId);
    }
  }

  /// <summary>
  /// Persists where the recording is being written, before any of it exists. Doing this up front
  /// is what makes an interrupted capture recoverable - the row points at the file even if the
  /// job never gets to run its own completion path.
  /// </summary>
  private async Task SetOutputFile(string jobId, string outputFile) {
    try {
      var recording = await _recordingRepository.GetByJobIdAsync(jobId);
      if (recording is null) {
        _logger.LogWarning("No recording found with JobId {JobId} to attach {OutputFile}", jobId, outputFile);
        return;
      }

      recording.FilePath = outputFile;
      await _recordingRepository.UpdateAsync(recording);
    } catch (Exception ex) {
      _logger.LogWarning(ex, "Failed to persist output file for JobId {JobId}", jobId);
    }
  }

  /// <summary>
  /// A capture that stopped early still leaves a playable fragmented MP4, so keep the file and
  /// call it partial. Only a capture that put nothing on disk is a genuine failure.
  /// </summary>
  private async Task FinaliseInterrupted(string jobId) {
    try {
      var recording = await _recordingRepository.GetByJobIdAsync(jobId);
      if (recording is null) {
        _logger.LogWarning("No recording found with JobId {JobId} to finalise", jobId);
        return;
      }

      var usable = RecordingFiles.HasUsableVideo(recording.FilePath);

      recording.Status = usable ? "partial" : "failed";
      recording.IsRecorded = usable;
      if (!usable) {
        recording.FilePath = null;
      }

      await SaveAndAnnounce(recording);
      _logger.LogInformation("Marked recording {JobId} as {Status}", jobId, recording.Status);
    } catch (Exception ex) {
      _logger.LogWarning(ex, "Failed to finalise recording for JobId {JobId}", jobId);
    }
  }

  /// <summary>
  /// Commits the row, then tells anyone listening. In that order, always: a client that refetches
  /// on the event has to find the new state already there, or the push is worse than useless.
  /// </summary>
  private async Task SaveAndAnnounce(Recording recording) {
    await _recordingRepository.UpdateAsync(recording);
    await _notifier.RecordingChangedAsync(recording, "status");
  }

  /// <summary>
  /// Best-effort status write. A status that cannot be persisted is worth a warning but must
  /// never abort an otherwise-healthy recording, so every failure here is swallowed.
  /// </summary>
  private async Task<Recording?> MarkStatus(string jobId, string status) {
    try {
      var recording = await _recordingRepository.GetByJobIdAsync(jobId);
      if (recording is null) {
        _logger.LogWarning("No recording found with JobId {JobId} to mark as {Status}", jobId, status);
        return null;
      }

      recording.Status = status;
      await SaveAndAnnounce(recording);
      _logger.LogInformation("Marked recording {JobId} as {Status}", jobId, status);
      return recording;
    } catch (Exception ex) {
      _logger.LogWarning(ex, "Failed to update recording status to {Status} for JobId {JobId}", status, jobId);
      return null;
    }
  }
}
