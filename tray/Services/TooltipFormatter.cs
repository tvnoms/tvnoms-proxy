namespace Tvnoms.Tray.Services;

/// <summary>Turns the current active-recording set into the tray icon's tooltip text. Pulled out
/// of TrayViewModel so the formatting rules are testable without any Avalonia dependency.</summary>
public static class TooltipFormatter {
  public static string Format(string listenAddress, IReadOnlyList<ActiveRecording> activeRecordings) {
    var header = $"Tvnoms Proxy - listening on {listenAddress}";
    if (activeRecordings.Count == 0) {
      return header;
    }

    var recordings = activeRecordings.Count == 1
      ? $"Recording: {FormatLine(activeRecordings[0])}"
      : $"Recording {activeRecordings.Count} shows:\n" + string.Join('\n', activeRecordings.Select(FormatLine));
    return $"{header}\n{recordings}";
  }

  private static string FormatLine(ActiveRecording recording) {
    if (recording.ElapsedSeconds is double elapsed && recording.DurationSeconds is double duration) {
      return $"{recording.Title} ({FormatMinutes(elapsed)}m / {FormatMinutes(duration)}m)";
    }
    return recording.Title;
  }

  private static int FormatMinutes(double seconds) => (int)(seconds / 60);
}
