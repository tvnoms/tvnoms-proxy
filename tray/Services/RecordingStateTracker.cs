using Tvnoms.Tray.Models;

namespace Tvnoms.Tray.Services;

/// <summary>A currently-recording show, with the most recent progress tick if one has arrived
/// yet (progress ticks land separately from the create/status event, roughly once a second).</summary>
public record ActiveRecording(Guid Id, string Title, double? ElapsedSeconds, double? DurationSeconds);

/// <summary>In-memory view of "what's recording right now", built from a startup/reconnect
/// snapshot and kept current by live SignalR events. No Avalonia dependency, so this is testable
/// in isolation.</summary>
public class RecordingStateTracker {
  private readonly Dictionary<Guid, string> _titles = new();
  private readonly Dictionary<Guid, (double Elapsed, double Duration)> _progress = new();
  private readonly Lock _gate = new();

  public event Action? Changed;

  public IReadOnlyList<ActiveRecording> ActiveRecordings {
    get {
      lock (_gate) {
        return _titles.Select(kv => {
          var hasProgress = _progress.TryGetValue(kv.Key, out var progress);
          return new ActiveRecording(
            kv.Key, kv.Value,
            hasProgress ? progress.Elapsed : null,
            hasProgress ? progress.Duration : null);
        }).ToList();
      }
    }
  }

  /// <summary>Replaces the whole set — used for the startup/reconnect snapshot from GET /recordings.
  /// The snapshot has no progress figures of its own, so progress is cleared and rebuilt from the
  /// next ticks.</summary>
  public void Reset(IEnumerable<RecordingDto> activeRecordings) {
    lock (_gate) {
      _titles.Clear();
      _progress.Clear();
      foreach (var recording in activeRecordings) {
        _titles[recording.Id] = recording.Title;
      }
    }
    Changed?.Invoke();
  }

  /// <summary>Applies a single live "RecordingChanged" event on top of the current snapshot.</summary>
  public void Apply(RecordingChangedEventDto changedEvent) {
    lock (_gate) {
      var isActive = changedEvent.Change != "deleted" && changedEvent.Status == "recording";
      if (isActive) {
        _titles[changedEvent.Id] = changedEvent.Title;
      } else {
        _titles.Remove(changedEvent.Id);
        _progress.Remove(changedEvent.Id);
      }
    }
    Changed?.Invoke();
  }

  /// <summary>Applies a live "RecordingProgress" tick. Does not raise <see cref="Changed"/> —
  /// ticks land roughly once a second per active recording, far too often to redraw a native
  /// tooltip on every one, so the caller decides when to read the updated figures back out via
  /// <see cref="ActiveRecordings"/>.</summary>
  public void ApplyProgress(RecordingProgressEventDto progress) {
    lock (_gate) {
      if (!_titles.ContainsKey(progress.Id)) {
        return;
      }
      _progress[progress.Id] = (progress.ElapsedSeconds, progress.DurationSeconds);
    }
  }
}
