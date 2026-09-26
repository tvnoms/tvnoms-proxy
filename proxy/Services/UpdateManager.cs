using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tvnoms.Proxy.Configuration;

#if WINDOWS
using System.ComponentModel;
using Microsoft.Win32;
using Velopack;
using Velopack.Sources;
#endif

namespace Tvnoms.Proxy.Services;

public class UpdateManager : IDisposable {
#if WINDOWS
  private const string AutostartName = "TvnomsProxy";

  private enum AutostartMode { Service, ScheduledTask, RunKey }

  private readonly ILogger<UpdateManager> _logger;
  private readonly Velopack.UpdateManager? _updateManager;
  private readonly string _updateUrl;

  public UpdateManager(ILogger<UpdateManager> logger, IConfiguration configuration) {
    _logger = logger;
    _updateUrl = configuration["UpdateUrl"] ?? "https://github.com/tvnoms/tvnoms-proxy";

    try {
      var source = new GithubSource(_updateUrl, null, false);
      _updateManager = new Velopack.UpdateManager(source);
      _logger.LogInformation("Update manager initialized with URL: {Url}", _updateUrl);
    } catch (Exception ex) {
      _logger.LogWarning(ex, "Failed to initialize update manager");
    }
  }

  public async Task<bool> CheckForUpdatesAsync() {
    if (_updateManager == null) {
      _logger.LogDebug("Update manager not available");
      return false;
    }

    try {
      _logger.LogInformation("Checking for updates...");
      var updateInfo = await _updateManager.CheckForUpdatesAsync();

      if (updateInfo != null) {
        _logger.LogInformation("Update available: {Version}", updateInfo.TargetFullRelease.Version);
        return true;
      }

      _logger.LogInformation("No updates available");
      return false;
    } catch (Exception ex) {
      _logger.LogError(ex, "Error checking for updates");
      return false;
    }
  }

  public async Task<bool> DownloadAndInstallUpdatesAsync() {
    if (_updateManager == null) {
      _logger.LogWarning("Update manager not available");
      return false;
    }

    try {
      _logger.LogInformation("Downloading updates...");

      // Check for updates first
      var updateInfo = await _updateManager.CheckForUpdatesAsync();
      if (updateInfo == null) {
        _logger.LogInformation("No updates available");
        return false;
      }

      // Download the update
      await _updateManager.DownloadUpdatesAsync(updateInfo);

      _logger.LogInformation("Update downloaded: {Version}. Installing and restarting...", updateInfo.TargetFullRelease.Version);

      // Apply the update and restart
      _updateManager.ApplyUpdatesAndRestart(updateInfo);

      return true;
    } catch (Exception ex) {
      _logger.LogError(ex, "Error downloading/installing updates");
      return false;
    }
  }

  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  public static void HandleVelopackEvents() {
    VelopackApp.Build()
        .OnFirstRun((v) => {
          try {
            RemoveDesktopShortcuts();

            // A marker from a prior run of this same installed version (e.g. the hook was
            // interrupted after choosing but before exiting) short-circuits the picker.
            var mode = ReadPersistedMode() ?? PromptForAutostartMode();
            InstallAutostart(mode);
            PersistMode(mode);

            // The tray icon is a separate, independent process from the proxy - launching it
            // here (rather than having the proxy itself spawn a GUI process) is what gets it
            // showing immediately post-install without requiring a logout/login. It registers
            // its own autostart entry (HKCU Run key) on its own first launch, so nothing further
            // is needed here for it to persist across reboots.
            LaunchTrayIcon();

            // Exit immediately after configuring autostart. Don't launch the GUI application.
            Environment.Exit(0);
          } catch (Exception ex) {
            LogDiagnostic($"Failed to configure autostart: {ex}");
            // Still exit to prevent GUI launch
            Environment.Exit(1);
          }
        })
        .OnAfterInstallFastCallback((v) => {
          // Quick post-install actions - remove shortcuts
          RemoveDesktopShortcuts();
        })
        .OnAfterUpdateFastCallback((v) => {
          try {
            RemoveDesktopShortcuts();

            // No marker means an install from before this feature existed, which always tried
            // to install a Service - defaulting to Service here preserves those installs.
            var mode = ReadPersistedMode() ?? AutostartMode.Service;
            switch (mode) {
              case AutostartMode.Service:
                RestartWindowsService();
                break;
              case AutostartMode.ScheduledTask:
              case AutostartMode.RunKey:
                // Neither is a Windows Service, so there's nothing to bounce via the SCM - the
                // scheduled task / Run key registration already points at Velopack's stable
                // "current" path (confirmed: it doesn't change across versions), so a plain
                // relaunch is all that's needed. This also avoids a UAC prompt on every update -
                // re-running the (now-elevated) Scheduled Task registration here would ask for
                // admin on every single auto-update, which defeats the point of it being a
                // lighter-weight option than Service.
                RelaunchProxy();
                break;
            }

            Environment.Exit(0);
          } catch (Exception ex) {
            LogDiagnostic($"Failed to restart after update: {ex}");
            Environment.Exit(1);
          }
        })
        .OnBeforeUninstallFastCallback((v) => {
          try {
            var mode = ReadPersistedMode() ?? AutostartMode.Service;
            UninstallAutostart(mode);
          } catch (Exception ex) {
            LogDiagnostic($"Failed to uninstall autostart: {ex}");
          }
        })
        .Run();
  }

  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  private static AutostartMode? ReadPersistedMode() {
    if (!File.Exists(AppPaths.AutostartModePath)) {
      return null;
    }

    return Enum.TryParse<AutostartMode>(File.ReadAllText(AppPaths.AutostartModePath).Trim(), out var mode)
      ? mode
      : null;
  }

  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  private static void PersistMode(AutostartMode mode) {
    Directory.CreateDirectory(AppPaths.AppDataDirectory);
    File.WriteAllText(AppPaths.AutostartModePath, mode.ToString());
  }

  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  private static void InstallAutostart(AutostartMode mode) {
    switch (mode) {
      case AutostartMode.Service:
        InstallWindowsService();
        break;
      case AutostartMode.ScheduledTask:
        InstallScheduledTask();
        break;
      case AutostartMode.RunKey:
        InstallRunKey();
        break;
    }
  }

  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  private static void UninstallAutostart(AutostartMode mode) {
    switch (mode) {
      case AutostartMode.Service:
        UninstallWindowsService();
        break;
      case AutostartMode.ScheduledTask:
        try {
          RunElevated(SchTasksPath, $"/Delete /TN \"{AutostartName}\" /F");
        } catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) {
          LogDiagnostic("Scheduled task deletion cancelled: UAC prompt declined.");
        }
        break;
      case AutostartMode.RunKey:
        using (var key = Registry.CurrentUser.OpenSubKey(
                 @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)) {
          key?.DeleteValue(AutostartName, throwOnMissingValue: false);
        }
        break;
    }
  }

  /// <summary>Shows the tray's autostart picker window and waits for the user's choice. Never
  /// throws - any failure (timeout, declined/closed picker, unreadable result) falls back to
  /// RunKey, the cheapest and most-certain-to-succeed option, since this is already a degraded
  /// path by the time it's hit.</summary>
  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  private static AutostartMode PromptForAutostartMode() {
    try {
      var exeDir = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName);
      if (string.IsNullOrEmpty(exeDir)) {
        return AutostartMode.RunKey;
      }

      var trayExePath = Path.Combine(exeDir, "tvnoms-tray.exe");
      if (!File.Exists(trayExePath)) {
        return AutostartMode.RunKey;
      }

      var outputPath = Path.Combine(Path.GetTempPath(), $"tvnoms-autostart-choice-{Guid.NewGuid():N}.txt");

      var startInfo = new System.Diagnostics.ProcessStartInfo {
        FileName = trayExePath,
        Arguments = $"--pick-autostart-mode \"{outputPath}\"",
        UseShellExecute = true
      };

      using var process = System.Diagnostics.Process.Start(startInfo);
      if (process is null || !process.WaitForExit(TimeSpan.FromMinutes(5))) {
        return AutostartMode.RunKey;
      }

      if (File.Exists(outputPath)) {
        var text = File.ReadAllText(outputPath).Trim();
        File.Delete(outputPath);
        if (Enum.TryParse<AutostartMode>(text, out var mode)) {
          return mode;
        }
      }
    } catch (Exception ex) {
      LogDiagnostic($"Autostart picker failed: {ex}");
    }

    return AutostartMode.RunKey;
  }

  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  private static void RemoveDesktopShortcuts() {
    try {
      var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
      var commonDesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

      // Remove shortcuts from user desktop
      var userShortcut = Path.Combine(desktopPath, "Tvnoms Proxy.lnk");
      if (File.Exists(userShortcut))
        File.Delete(userShortcut);

      var userShortcut2 = Path.Combine(desktopPath, "TvnomsProxy.lnk");
      if (File.Exists(userShortcut2))
        File.Delete(userShortcut2);

      // Remove shortcuts from all users desktop
      var commonShortcut = Path.Combine(commonDesktopPath, "Tvnoms Proxy.lnk");
      if (File.Exists(commonShortcut))
        File.Delete(commonShortcut);

      var commonShortcut2 = Path.Combine(commonDesktopPath, "TvnomsProxy.lnk");
      if (File.Exists(commonShortcut2))
        File.Delete(commonShortcut2);
    } catch (Exception ex) {
      System.Diagnostics.Debug.WriteLine($"Failed to remove desktop shortcuts: {ex.Message}");
    }
  }

  /// <summary>Installing/starting a service always requires admin rights, but OnFirstRun runs as
  /// the installing standard user (the installer is deliberately non-admin) - so these two calls
  /// need their own UAC elevation via Verb="runas". That requires UseShellExecute=true, which is
  /// incompatible with output redirection, so unlike the (unelevated) calls below this can't
  /// capture stdout/stderr.</summary>
  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  private static void InstallWindowsService() {
    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
    if (string.IsNullOrEmpty(exePath))
      return;

    try {
      var createExitCode = RunElevated(ScPath,
        $"create {AutostartName} binPath= \"{exePath}\" DisplayName= \"Tvnoms Proxy Service\" start= auto");

      if (createExitCode == 0) {
        RunElevated(ScPath, $"start {AutostartName}");
      }
    } catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) {
      // ERROR_CANCELLED - the user declined the UAC prompt.
      LogDiagnostic("Service install cancelled: UAC prompt declined.");
    }
  }

  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  private static void InstallScheduledTask() {
    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
    if (string.IsNullOrEmpty(exePath)) {
      return;
    }

    // Confirmed on a real machine: schtasks /Create requires an elevated token to register any
    // new task at all, regardless of /RU - a plain unelevated call fails "Access is denied" even
    // naming the calling user via /RU. So this needs the same runas treatment as the Service path,
    // despite being scoped to the current user's own session rather than running as SYSTEM.
    LogDiagnostic($"Creating scheduled task for '{exePath}'.");
    try {
      RunElevated(SchTasksPath,
        $"/Create /TN \"{AutostartName}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL LIMITED /F");
      RunElevated(SchTasksPath, $"/Run /TN \"{AutostartName}\"");
    } catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) {
      // ERROR_CANCELLED - the user declined the UAC prompt.
      LogDiagnostic("Scheduled task creation cancelled: UAC prompt declined.");
    }
  }

  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  private static void InstallRunKey() {
    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
    if (string.IsNullOrEmpty(exePath)) {
      return;
    }

    using (var key = Registry.CurrentUser.OpenSubKey(
             @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)) {
      key?.SetValue(AutostartName, $"\"{exePath}\"", RegistryValueKind.String);
    }

    RelaunchProxy();
  }

  /// <summary>Starts the current build of the proxy exe, unelevated. Used both right after
  /// installing the Run key (no service/task to start it for us) and after an auto-update in
  /// ScheduledTask/RunKey mode (neither is a Windows Service, so there's nothing for the SCM to
  /// restart).</summary>
  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  private static void RelaunchProxy() {
    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
    if (string.IsNullOrEmpty(exePath)) {
      return;
    }

    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
      FileName = exePath,
      UseShellExecute = true
    });
  }

  private static string ScPath => Path.Combine(Environment.SystemDirectory, "sc.exe");
  private static string SchTasksPath => Path.Combine(Environment.SystemDirectory, "schtasks.exe");

  /// <summary>Runs a command and logs its exit code + output when it fails. This whole hook runs
  /// before Serilog is configured (Program.cs builds the host after HandleVelopackEvents), so
  /// failures here would otherwise be completely invisible - LogDiagnostic writes a plain-text
  /// fallback log instead.</summary>
  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  private static void RunProcess(string fileName, string arguments) {
    using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
      FileName = fileName,
      Arguments = arguments,
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true
    });

    if (process is null) {
      LogDiagnostic($"Failed to start: {fileName} {arguments}");
      return;
    }

    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode != 0) {
      LogDiagnostic(
        $"'{fileName} {arguments}' exited {process.ExitCode}. stdout: {stdout.Trim()} stderr: {stderr.Trim()}");
    }
  }

  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  private static void LogDiagnostic(string message) {
    System.Diagnostics.Debug.WriteLine(message);
    try {
      Directory.CreateDirectory(AppPaths.LogsDirectory);
      File.AppendAllText(
        Path.Combine(AppPaths.LogsDirectory, "autostart-setup.log"),
        $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
    } catch {
      // Best-effort diagnostics only - this must never be why autostart setup fails.
    }
  }

  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  private static void LaunchTrayIcon() {
    try {
      var exeDir = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName);
      if (string.IsNullOrEmpty(exeDir)) {
        return;
      }

      var trayExePath = Path.Combine(exeDir, "tvnoms-tray.exe");
      if (!File.Exists(trayExePath)) {
        return;
      }

      System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
        FileName = trayExePath,
        UseShellExecute = true,
      });
    } catch (Exception ex) {
      System.Diagnostics.Debug.WriteLine($"Failed to launch tray icon: {ex.Message}");
    }
  }

  /// <summary>Runs inside the proxy process itself, which in Service mode IS the service - i.e.
  /// this executes as LocalSystem in session 0. LocalSystem already has full SCM rights (no
  /// elevation needed or possible - session 0 has no window station to render a UAC prompt on
  /// anyway), so unlike Install/UninstallWindowsService this stays unelevated.</summary>
  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  private static void RestartWindowsService() {
    RunProcess(ScPath, $"stop {AutostartName}");
    System.Threading.Thread.Sleep(2000); // Wait for service to stop
    RunProcess(ScPath, $"start {AutostartName}");
  }

  /// <summary>Runs from OnBeforeUninstallFastCallback as the interactive standard user (uninstall
  /// isn't elevated any more than install is), so - like InstallWindowsService - these need their
  /// own UAC elevation.</summary>
  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  private static void UninstallWindowsService() {
    try {
      RunElevated(ScPath, $"stop {AutostartName}");
      System.Threading.Thread.Sleep(2000); // Wait for service to stop
      RunElevated(ScPath, $"delete {AutostartName}");
    } catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) {
      LogDiagnostic("Service uninstall cancelled: UAC prompt declined.");
    }
  }

  /// <summary>Runs a command elevated via a UAC prompt. Throws Win32Exception (NativeErrorCode
  /// 1223 / ERROR_CANCELLED) if the prompt is declined - callers must catch that.</summary>
  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  private static int? RunElevated(string fileName, string arguments) {
    using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
      FileName = fileName,
      Arguments = arguments,
      UseShellExecute = true,
      Verb = "runas",
      WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
    });
    process?.WaitForExit();

    // UseShellExecute=true means stdout/stderr can't be captured here (unlike RunProcess) - the
    // exit code is all there is to go on.
    if (process?.ExitCode is int code and not 0) {
      LogDiagnostic($"'{fileName} {arguments}' (elevated) exited {code}.");
    }

    return process?.ExitCode;
  }

  public void Dispose() {
    // Velopack UpdateManager doesn't implement IDisposable
    // No cleanup needed
  }
#else
  // Dummy implementation for non-Windows platforms
  private readonly ILogger<UpdateManager> _logger;

  public UpdateManager(ILogger<UpdateManager> logger, IConfiguration configuration) {
    _logger = logger;
    _logger.LogInformation("Update manager not available on this platform");
  }

  public Task<bool> CheckForUpdatesAsync() {
    return Task.FromResult(false);
  }

  public Task<bool> DownloadAndInstallUpdatesAsync() {
    return Task.FromResult(false);
  }

  public static void HandleVelopackEvents() {
    // No-op on non-Windows platforms
  }

  public void Dispose() {
    // No-op
  }
#endif
}
