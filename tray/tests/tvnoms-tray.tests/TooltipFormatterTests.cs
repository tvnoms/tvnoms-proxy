using Tvnoms.Tray.Services;
using Xunit;

namespace Tvnoms.Tray.Tests;

public class TooltipFormatterTests {
  private const string Address = "192.168.1.10:8963";
  private const string Header = "Tvnoms Proxy - listening on 192.168.1.10:8963";

  [Fact]
  public void No_active_recordings_shows_listen_address_only() {
    var text = TooltipFormatter.Format(Address, []);

    Assert.Equal(Header, text);
  }

  [Fact]
  public void Single_recording_without_progress_shows_title_only() {
    var active = new[] { new ActiveRecording(Guid.NewGuid(), "Show A", null, null) };

    var text = TooltipFormatter.Format(Address, active);

    Assert.Equal($"{Header}\nRecording: Show A", text);
  }

  [Fact]
  public void Single_recording_with_progress_shows_elapsed_and_duration_in_minutes() {
    var active = new[] { new ActiveRecording(Guid.NewGuid(), "Show A", 720, 3600) };

    var text = TooltipFormatter.Format(Address, active);

    Assert.Equal($"{Header}\nRecording: Show A (12m / 60m)", text);
  }

  [Fact]
  public void Multiple_recordings_are_listed_one_per_line_with_a_header() {
    var active = new[] {
      new ActiveRecording(Guid.NewGuid(), "Show A", 720, 3600),
      new ActiveRecording(Guid.NewGuid(), "Show B", null, null),
    };

    var text = TooltipFormatter.Format(Address, active);

    Assert.Equal($"{Header}\nRecording 2 shows:\nShow A (12m / 60m)\nShow B", text);
  }

  [Fact]
  public void Minutes_are_truncated_not_rounded() {
    // 89 seconds is 1m29s — should read as 1m, not round up to 2m.
    var active = new[] { new ActiveRecording(Guid.NewGuid(), "Show A", 89, 3600) };

    var text = TooltipFormatter.Format(Address, active);

    Assert.Equal($"{Header}\nRecording: Show A (1m / 60m)", text);
  }
}
