using Tvnoms.Tray.Models;
using Tvnoms.Tray.Services;
using Xunit;

namespace Tvnoms.Tray.Tests;

public class RecordingStateTrackerTests {
  [Fact]
  public void Reset_seeds_active_recordings_from_snapshot() {
    var tracker = new RecordingStateTracker();
    var id = Guid.NewGuid();

    tracker.Reset([new RecordingDto { Id = id, Title = "Show A", Status = "recording" }]);

    Assert.Single(tracker.ActiveRecordings);
    Assert.Equal("Show A", tracker.ActiveRecordings.Single(r => r.Id == id).Title);
  }

  [Fact]
  public void Apply_adds_recording_when_status_becomes_recording() {
    var tracker = new RecordingStateTracker();
    var id = Guid.NewGuid();

    tracker.Apply(new RecordingChangedEventDto { Id = id, Title = "Show A", Status = "recording", Change = "status" });

    Assert.Single(tracker.ActiveRecordings);
  }

  [Fact]
  public void Apply_removes_recording_when_status_leaves_recording() {
    var tracker = new RecordingStateTracker();
    var id = Guid.NewGuid();
    tracker.Reset([new RecordingDto { Id = id, Title = "Show A", Status = "recording" }]);

    tracker.Apply(new RecordingChangedEventDto { Id = id, Title = "Show A", Status = "complete", Change = "status" });

    Assert.Empty(tracker.ActiveRecordings);
  }

  [Fact]
  public void Apply_removes_recording_on_delete() {
    var tracker = new RecordingStateTracker();
    var id = Guid.NewGuid();
    tracker.Reset([new RecordingDto { Id = id, Title = "Show A", Status = "recording" }]);

    tracker.Apply(new RecordingChangedEventDto { Id = id, Title = "Show A", Status = "recording", Change = "deleted" });

    Assert.Empty(tracker.ActiveRecordings);
  }

  [Fact]
  public void ApplyProgress_sets_elapsed_and_duration_for_an_active_recording() {
    var tracker = new RecordingStateTracker();
    var id = Guid.NewGuid();
    tracker.Reset([new RecordingDto { Id = id, Title = "Show A", Status = "recording" }]);

    tracker.ApplyProgress(new RecordingProgressEventDto {
      Id = id, ElapsedSeconds = 720, DurationSeconds = 3600,
    });

    var recording = tracker.ActiveRecordings.Single(r => r.Id == id);
    Assert.Equal(720, recording.ElapsedSeconds);
    Assert.Equal(3600, recording.DurationSeconds);
  }

  [Fact]
  public void ApplyProgress_is_ignored_for_a_recording_that_is_not_active() {
    var tracker = new RecordingStateTracker();

    tracker.ApplyProgress(new RecordingProgressEventDto {
      Id = Guid.NewGuid(), ElapsedSeconds = 10, DurationSeconds = 100,
    });

    Assert.Empty(tracker.ActiveRecordings);
  }

  [Fact]
  public void Changed_event_fires_on_reset_and_apply() {
    var tracker = new RecordingStateTracker();
    var changeCount = 0;
    tracker.Changed += () => changeCount++;

    tracker.Reset([]);
    tracker.Apply(new RecordingChangedEventDto { Id = Guid.NewGuid(), Title = "Show A", Status = "recording", Change = "status" });

    Assert.Equal(2, changeCount);
  }
}
