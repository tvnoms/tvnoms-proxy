namespace Tvnoms.Proxy.Models;

/// <summary>
/// Pushed whenever a recording row changes state. The web treats this as a signal to refetch
/// rather than as the source of truth, so this payload can grow without breaking any client
/// already consuming it - and a client that has never heard of a new <see cref="Change"/> value
/// still refetches and ends up correct.
/// </summary>
public record RecordingChangedEvent {
  public required Guid Id { get; init; }
  public required string JobId { get; init; }
  public required string Title { get; init; }

  /// <summary>"pending" | "recording" | "complete" | "partial" | "failed"</summary>
  public required string Status { get; init; }

  /// <summary>"created" | "status" | "deleted"</summary>
  public required string Change { get; init; }

  public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Position of an in-flight capture. Always absolute, never a delta, so a client that drops a
/// tick loses nothing and one that misses the whole stream just falls back to the wall clock.
/// Deliberately never persisted - a stale tick read back later would be a lie.
/// </summary>
public record RecordingProgressEvent {
  public required Guid Id { get; init; }
  public required string JobId { get; init; }

  /// <summary>Media time ffmpeg has actually written. Stops advancing when the input stalls.</summary>
  public required double CapturedSeconds { get; init; }

  /// <summary>Wall clock since capture began. Diverges from CapturedSeconds on a stalled input.</summary>
  public required double ElapsedSeconds { get; init; }

  /// <summary>Total scheduled duration handed to ffmpeg.</summary>
  public required double DurationSeconds { get; init; }

  public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
}
