using Dapper.Contrib.Extensions;

namespace Tvnoms.Proxy.Data.Models;

[Table("recordings")]
public record Recording {
  [ExplicitKey]
  public Guid Id { get; set; }

  public required string JobId { get; set; }

  public required string Url { get; set; }
  public required string Title { get; set; }
  public DateTimeOffset StartTime { get; set; }
  public DateTimeOffset EndTime { get; set; }
  public bool IsRecorded { get; set; }
  public string? FilePath { get; set; }

  /// <summary>
  /// Status of the recording: "pending", "recording", "complete", "partial", or "failed"
  /// pending = scheduled but not yet recorded
  /// recording = ffmpeg is actively capturing
  /// complete = successfully recorded the full duration
  /// partial = recording was interrupted (user cancelled)
  /// failed = recording encountered an error
  /// </summary>
  public string Status { get; set; } = "pending";
}
