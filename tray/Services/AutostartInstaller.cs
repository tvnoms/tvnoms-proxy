using System.Runtime.Versioning;
using Microsoft.Win32;
using Tvnoms.Tray.Configuration;

namespace Tvnoms.Tray.Services;

/// <summary>Self-installs a per-user autostart entry on first successful run, mirroring the
/// precedent already set by tvnoms-proxy's own Velopack first-run hook (which self-installs
/// the Windows Service without prompting). A marker file prevents re-writing on every launch and
/// lets the user remove their own autostart entry without this fighting them.
///
/// Linux is deliberately not handled here: the packaged tray is a systemd user unit bound to
/// tvnoms-proxy.service, which is what makes it start and stop with the proxy. A self-installed
/// XDG autostart entry would start a second, unmanaged copy at login that outlives the proxy.
///
/// macOS support is implemented but not yet wired into any packaging/installer — tvnoms-proxy
/// itself has no macOS packaging pipeline today, so shipping a signed/notarized .app is a
/// separate, later effort. This code path exists so it's ready when that happens.</summary>
public static class AutostartInstaller {
  public static void EnsureInstalled() {
    if (OperatingSystem.IsLinux() || File.Exists(TrayPaths.AutostartMarkerPath)) {
      return;
    }

    try {
      if (OperatingSystem.IsWindows()) {
        InstallWindows();
      } else if (OperatingSystem.IsMacOS()) {
        InstallMacOs();
      }

      Directory.CreateDirectory(TrayPaths.AppDataDirectory);
      File.WriteAllText(TrayPaths.AutostartMarkerPath, DateTimeOffset.UtcNow.ToString("O"));
    } catch {
      // Autostart is a convenience, not a requirement — a failure here (e.g. no write access to
      // the registry or autostart directory) should never stop the tray icon itself from running.
    }
  }

  [SupportedOSPlatform("windows")]
  private static void InstallWindows() {
    var exePath = Environment.ProcessPath;
    if (string.IsNullOrEmpty(exePath)) {
      return;
    }

    using var key = Registry.CurrentUser.OpenSubKey(
      @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
    key?.SetValue("TvnomsTray", $"\"{exePath}\"", RegistryValueKind.String);
  }

  [SupportedOSPlatform("macos")]
  private static void InstallMacOs() {
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var launchAgentsDir = Path.Combine(home, "Library", "LaunchAgents");
    Directory.CreateDirectory(launchAgentsDir);

    var exePath = Environment.ProcessPath ?? "/Applications/Tvnoms Tray.app/Contents/MacOS/tvnoms-tray";
    var plist =
      $"""
       <?xml version="1.0" encoding="UTF-8"?>
       <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
       <plist version="1.0"><dict>
         <key>Label</key><string>com.tvnoms.tray</string>
         <key>ProgramArguments</key><array><string>{exePath}</string></array>
         <key>RunAtLoad</key><true/>
         <key>KeepAlive</key><false/>
       </dict></plist>
       """;

    File.WriteAllText(Path.Combine(launchAgentsDir, "com.tvnoms.tray.plist"), plist);
  }
}
