using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Tvnoms.Tray.Services;

namespace Tvnoms.Tray;

internal static class Program {
  [STAThread]
  public static void Main(string[] args) {
    UseHiDpiOnX11IfUnconfigured();
    AutostartInstaller.EnsureInstalled();
    BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
  }

  // This project has no Avalonia.Wayland package reference, so on Linux it always renders via
  // X11 (XWayland when the session is actually Wayland). That backend only picks up a HiDPI
  // scale factor from Xft.dpi, a QT_* env var, or an explicit AVALONIA_* override. None of those
  // are set by default on a lot of compositors (e.g. COSMIC doesn't propagate its output scale to
  // XWayland clients), and Avalonia's own physical-DPI auto-detection doesn't help here either -
  // it queries the X11 root window's physical size, which XWayland reports incorrectly (a known
  // XWayland quirk; verified empirically - AVALONIA_USE_PHYSICAL_DPI made no difference on this
  // monitor even though `xrandr`'s per-output physical size is correct). So: compute a scale
  // factor ourselves from `xrandr`'s per-output data - the same source a user would eyeball to
  // pick a scale manually - and only apply it when nothing else already configured one.
  private static void UseHiDpiOnX11IfUnconfigured() {
    if (!OperatingSystem.IsLinux()) {
      return;
    }

    // Note: QT_AUTO_SCREEN_SCALE_FACTOR is deliberately excluded - it's a boolean "let Qt
    // auto-detect" toggle, not an actual factor, and COSMIC's session env sets it to 1 by
    // default regardless of whether scaling was ever configured. Treating its mere presence
    // as "already configured" was silently defeating this whole workaround.
    string[] scaleHintVars = [
      "AVALONIA_GLOBAL_SCALE_FACTOR", "AVALONIA_SCREEN_SCALE_FACTORS", "AVALONIA_USE_PHYSICAL_DPI",
      "QT_SCALE_FACTOR", "QT_SCREEN_SCALE_FACTORS"
    ];
    if (scaleHintVars.Any(v => Environment.GetEnvironmentVariable(v) is not null)) {
      return;
    }

    var scale = TryComputeScaleFromXrandr();
    if (scale.HasValue) {
      Console.WriteLine($"[tvnoms-tray] No display scaling configured; using {scale.Value}x computed from xrandr.");
      Environment.SetEnvironmentVariable("AVALONIA_GLOBAL_SCALE_FACTOR", scale.Value.ToString(CultureInfo.InvariantCulture));
    }
  }

  /// <summary>Best-effort only: no xrandr, an unparsable connected-output line, or a bogus
  /// physical size just means we leave scaling exactly as Avalonia would have picked it anyway.</summary>
  private static double? TryComputeScaleFromXrandr() {
    try {
      var psi = new ProcessStartInfo("xrandr", "--current") {
        RedirectStandardOutput = true,
        UseShellExecute = false
      };
      using var proc = Process.Start(psi);
      if (proc is null) {
        return null;
      }

      var output = proc.StandardOutput.ReadToEnd();
      if (!proc.WaitForExit(2000)) {
        return null;
      }

      // Prefer the line marked "primary"; fall back to the first connected output otherwise.
      var match = Regex.Match(output, @"connected primary (\d+)x(\d+)\+\d+\+\d+[^\n]*?(\d+)mm x (\d+)mm");
      if (!match.Success) {
        match = Regex.Match(output, @"connected (\d+)x(\d+)\+\d+\+\d+[^\n]*?(\d+)mm x (\d+)mm");
      }
      if (!match.Success) {
        return null;
      }

      var widthPx = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
      var widthMm = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
      if (widthMm <= 0) {
        return null;
      }

      var ppi = widthPx / (widthMm / 25.4);
      // Snap to quarter-steps and clamp to a sane range, rather than trust a raw ratio that a
      // slightly-off EDID physical size could otherwise push arbitrarily high or low.
      return Math.Clamp(Math.Round(ppi / 96 * 4) / 4, 1.0, 3.0);
    } catch {
      return null;
    }
  }

  public static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
      .UsePlatformDetect()
      .LogToTrace();
}
