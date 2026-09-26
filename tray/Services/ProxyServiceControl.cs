using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Tvnoms.Tray.Services;

/// <summary>Starts and stops tvnoms-proxy from the tray menu, using whatever mechanism the
/// proxy was installed under on this OS. Every call runs off the UI thread and never throws — a
/// failed or declined start/stop just leaves the proxy as it was, and the tray's connection state
/// (not the return value) is what the menu reflects.</summary>
public static class ProxyServiceControl {
  private const string SystemdUnit = "tvnoms-proxy.service";

  // Must match the name UpdateManager registers with sc.exe / schtasks.exe / the registry Run key.
  private const string WindowsServiceName = "TvnomsProxy";

  // The name Process.GetProcessesByName expects - no ".exe", since Process.ProcessName strips it.
  private const string ProxyProcessName = "tvnoms-proxy";

  public static bool IsSupported => OperatingSystem.IsLinux() || OperatingSystem.IsWindows();

  public static Task<bool> StartAsync() => Task.Run(() => Run(start: true));

  public static Task<bool> StopAsync() => Task.Run(() => Run(start: false));

  private static bool Run(bool start) {
    try {
      if (OperatingSystem.IsLinux()) {
        return RunProcess("systemctl", $"--user {(start ? "start" : "stop")} {SystemdUnit}") == 0;
      }
      if (OperatingSystem.IsWindows()) {
        return RunWindows(start);
      }
    } catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) {
      // ERROR_CANCELLED - the user declined the UAC prompt.
    } catch (Exception ex) {
      Console.WriteLine($"[tvnoms-tray] Failed to {(start ? "start" : "stop")} proxy: {ex.Message}");
    }
    return false;
  }

  /// <summary>Branches on the autostart mode picked at first run, the same way UpdateManager does
  /// on the proxy side. No marker at all means an install from before the picker existed, which
  /// always installed a Service.</summary>
  [SupportedOSPlatform("windows")]
  private static bool RunWindows(bool start) {
    var mode = ProxyDiscovery.ReadProxyAutostartMode();
    if (mode is "ScheduledTask" or "RunKey") {
      return start ? StartProxyProcess() : StopProxyProcesses();
    }

    // Starting/stopping a service needs admin rights, so this goes through a UAC prompt, just
    // like the service install/uninstall in UpdateManager.
    return RunElevated(Path.Combine(Environment.SystemDirectory, "sc.exe"),
      $"{(start ? "start" : "stop")} {WindowsServiceName}") == 0;
  }

  /// <summary>ScheduledTask/RunKey mode runs the proxy as a plain per-user process, so it can be
  /// launched directly and unelevated - the same thing UpdateManager.RelaunchProxy does after an
  /// update. The tray ships alongside the proxy in Velopack's app directory.</summary>
  [SupportedOSPlatform("windows")]
  private static bool StartProxyProcess() {
    if (Process.GetProcessesByName(ProxyProcessName).Length > 0) {
      return true;
    }

    var exePath = Path.Combine(AppContext.BaseDirectory, $"{ProxyProcessName}.exe");
    if (!File.Exists(exePath)) {
      return false;
    }

    using var process = Process.Start(new ProcessStartInfo { FileName = exePath, UseShellExecute = true });
    return process is not null;
  }

  /// <summary>A plain process has no console to send Ctrl+C to and no SCM stop to honour, so it's
  /// killed outright - along with any ffmpeg children, which would otherwise keep recording with
  /// nothing managing them.</summary>
  [SupportedOSPlatform("windows")]
  private static bool StopProxyProcesses() {
    foreach (var process in Process.GetProcessesByName(ProxyProcessName)) {
      using (process) {
        process.Kill(entireProcessTree: true);
      }
    }
    return true;
  }

  private static int RunProcess(string fileName, string arguments) {
    using var process = Process.Start(new ProcessStartInfo {
      FileName = fileName,
      Arguments = arguments,
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardError = true
    });
    if (process is null) {
      return -1;
    }

    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0) {
      Console.WriteLine($"[tvnoms-tray] '{fileName} {arguments}' exited {process.ExitCode}: {stderr.Trim()}");
    }
    return process.ExitCode;
  }

  /// <summary>Throws Win32Exception (NativeErrorCode 1223) if the UAC prompt is declined.</summary>
  [SupportedOSPlatform("windows")]
  private static int RunElevated(string fileName, string arguments) {
    using var process = Process.Start(new ProcessStartInfo {
      FileName = fileName,
      Arguments = arguments,
      UseShellExecute = true,
      Verb = "runas",
      WindowStyle = ProcessWindowStyle.Hidden
    });
    if (process is null) {
      return -1;
    }

    process.WaitForExit();
    return process.ExitCode;
  }
}
