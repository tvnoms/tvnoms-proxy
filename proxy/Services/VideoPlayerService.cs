using System.Diagnostics;
using System.Security;
using System.Collections.Concurrent;
using Tvnoms.Proxy.Data.Repositories;

namespace Tvnoms.Proxy.Services;

public class VideoPlayerService(
  ILogger<VideoPlayerService> logger,
  ISettingsRepository settingsRepository)
  : IVideoPlayerService {
  private static readonly ConcurrentDictionary<string, TrackedPlayerProcess> ActivePlayersByExecutable =
    new(StringComparer.OrdinalIgnoreCase);

  private static readonly SemaphoreSlim PlayerLaunchLock = new(1, 1);

  public async Task<PlayerResult> PlayFromUrl(string url, CancellationToken cancellationToken = default) {
    if (string.IsNullOrWhiteSpace(url)) {
      logger.LogWarning("PlayFromUrl called with empty URL");
      return PlayerResult.ClientFail("URL must not be empty.");
    }

    try {
      var pathOrUrl = url;
      if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) {
        //This is a recording, need to sanitise the path
        pathOrUrl = url.Substring(7); // Remove "file://" prefix

        // On Windows, handle file:///C:/path format
        if (OperatingSystem.IsWindows() && pathOrUrl.StartsWith($"/") &&
            pathOrUrl.Length > 2 && pathOrUrl[2] == ':') {
          pathOrUrl = pathOrUrl[1..]; // Remove leading slash for Windows paths
          //TODO: Revisit all this on an actual Windows installation
        }

        if (!File.Exists(pathOrUrl)) {
          logger.LogError("Local file not found: {Path}", pathOrUrl);
          return PlayerResult.ClientFail($"Local file not found: {pathOrUrl}");
        }

        logger.LogDebug("Playing local file: {Path}", pathOrUrl);
      }

      var exe = await settingsRepository.GetSettingAsync("MediaPlayerPath");
      var playerArguments = BuildPlayerArguments(
        await settingsRepository.GetSettingAsync("MediaPlayerArguments"),
        pathOrUrl);

      if (string.IsNullOrWhiteSpace(exe)) {
        logger.LogError("Video player executable not configured");
        return PlayerResult.ServerFail(
          "Video player executable is not configured. Please set MediaPlayerPath in settings.");
      }

      if (!File.Exists(exe)) {
        logger.LogWarning("Configured video player executable not found: {Path}", exe);
        return PlayerResult.ServerFail($"Video player executable not found at path: {exe}");
      }

      var executableKey = NormalizeExecutablePath(exe);

      await PlayerLaunchLock.WaitAsync(cancellationToken);
      try {
        StopTrackedPlayer(executableKey);

        var psi = new ProcessStartInfo {
          FileName = exe,
          CreateNoWindow = true,
          UseShellExecute = false,
          RedirectStandardError = true,
          RedirectStandardOutput = true
        };

        foreach (var argument in playerArguments) {
          psi.ArgumentList.Add(argument);
        }

        logger.LogDebug("Starting player: {FileName} {Arguments}", psi.FileName,
          FormatArgumentsForLog(psi.ArgumentList));

        var process = Process.Start(psi);
        if (process == null) {
          logger.LogError("Failed to start video player process");
          return PlayerResult.ServerFail("Failed to start video player process.");
        }

        var stderrLines = new ConcurrentBag<string>();
        process.ErrorDataReceived += (_, e) => {
          if (string.IsNullOrEmpty(e.Data)) {
            return;
          }

          stderrLines.Add(e.Data);
          logger.LogWarning("Player stderr: {Message}", e.Data);
        };
        process.OutputDataReceived += (_, e) => {
          if (!string.IsNullOrEmpty(e.Data)) {
            logger.LogDebug("Player stdout: {Message}", e.Data);
          }
        };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        // Wait briefly to detect an immediate crash (e.g. bad arguments, missing display, codec error).
        var exited = await Task.Run(() => process.WaitForExit(500), cancellationToken);
        if (exited && process.ExitCode != 0) {
          var stderr = string.Join(" | ", stderrLines);
          var detail = string.IsNullOrWhiteSpace(stderr)
            ? $"exit code {process.ExitCode}"
            : stderr;
          logger.LogError("Player exited immediately with code {ExitCode}: {Detail}", process.ExitCode, detail);
          process.Dispose();
          return PlayerResult.ServerFail($"Player exited immediately: {detail}");
        }

        ActivePlayersByExecutable[executableKey] =
          new TrackedPlayerProcess(process.Id, process.StartTime.ToUniversalTime());
        // Hand off ownership — do not dispose here; the process runs independently.
        _ = process.Handle;
        return PlayerResult.Ok();
      } catch (Exception ex) {
        logger.LogError(ex, "Failed to start video player process");
        return PlayerResult.ServerFail($"Failed to start video player: {ex.Message}");
      } finally {
        PlayerLaunchLock.Release();
      }
    } catch (OperationCanceledException) {
      logger.LogInformation("PlayFromUrl canceled");
      return PlayerResult.ServerFail("Request was canceled.");
    } catch (Exception ex) {
      logger.LogError(ex, "Error while starting video player");
      return PlayerResult.ServerFail($"Unexpected error while starting video player: {ex.Message}");
    }
  }

  /// <summary>
  /// Attempt to open the recordings folder in the system file browser
  /// The will be initiated from a POST in the browser
  /// </summary>
  /// <param name="cancellationToken"></param>
  /// <returns></returns>
  public async Task<PlayerResult> OpenRecordingsFolderAsync(CancellationToken cancellationToken = default) {
    try {
      var recordingsPath = await settingsRepository.GetSettingAsync("RecordingsPath");

      if (string.IsNullOrWhiteSpace(recordingsPath)) {
        logger.LogError("Recordings path not configured");
        return PlayerResult.ServerFail("Recordings path is not configured. Please set RecordingsPath in settings.");
      }

      if (!Directory.Exists(recordingsPath)) {
        logger.LogWarning("Recordings directory does not exist: {Path}", recordingsPath);
        try {
          Directory.CreateDirectory(recordingsPath);
          logger.LogInformation("Created recordings directory: {Path}", recordingsPath);
        } catch (Exception ex) {
          logger.LogError(ex, "Failed to create recordings directory: {Path}", recordingsPath);
          return PlayerResult.ServerFail($"Recordings directory does not exist and could not be created: {ex.Message}");
        }
      }

      var psi = new ProcessStartInfo();

      if (OperatingSystem.IsWindows()) {
        psi.FileName = "explorer.exe";
        psi.Arguments = recordingsPath;
      } else if (OperatingSystem.IsLinux()) {
        psi.FileName = "xdg-open";
        psi.Arguments = QuoteArgument(recordingsPath);
        psi.UseShellExecute = false;
      } else if (OperatingSystem.IsMacOS()) {
        psi.FileName = "open";
        psi.Arguments = QuoteArgument(recordingsPath);
        psi.UseShellExecute = false;
      } else {
        logger.LogError("Unsupported operating system");
        return PlayerResult.ServerFail("Unsupported operating system.");
      }

      psi.CreateNoWindow = true;

      logger.LogDebug("Opening recordings folder: {Path} with {Command} {Arguments}",
        recordingsPath, psi.FileName, psi.Arguments);

      using var process = Process.Start(psi);
      if (process != null) {
        return PlayerResult.Ok();
      }

      logger.LogError("Failed to start file browser process");
      return PlayerResult.ServerFail("Failed to open file browser process.");
    } catch (OperationCanceledException) {
      logger.LogInformation("OpenRecordingsFolderAsync canceled");
      return PlayerResult.ServerFail("Request was canceled.");
    } catch (Exception ex) {
      logger.LogError(ex, "Error while opening recordings folder");
      return PlayerResult.ServerFail($"Unexpected error while opening recordings folder: {ex.Message}");
    }
  }

  /// <summary>
  /// Safely quote and sanitize command arguments
  /// </summary>
  private static string QuoteArgument(string arg) {
    if (string.IsNullOrEmpty(arg))
      return "\"\"";

    // Remove potentially dangerous characters
    var sanitized = arg.Replace("\"", "\\\"")
      .Replace(";", "")
      .Replace("&", "")
      .Replace("|", "")
      .Replace("`", "")
      .Replace("$", "")
      .Replace("(", "")
      .Replace(")", "");

    // Always quote to prevent injection
    return $"\"{sanitized}\"";
  }

  /// <summary>
  /// Validate and sanitize MPV arguments template
  /// </summary>
  private static IReadOnlyList<string> BuildPlayerArguments(string argumentTemplate, string url) {
    if (string.IsNullOrWhiteSpace(argumentTemplate)) {
      return [url];
    }

    var parsedArguments = SplitArguments(argumentTemplate);
    if (parsedArguments.Any(IsDangerousPlayerArgument)) {
      throw new SecurityException("Potentially dangerous MPV arguments detected");
    }

    var hasUrlPlaceholder = false;
    for (var i = 0; i < parsedArguments.Count; i++) {
      if (!parsedArguments[i].Contains("{{URL}}", StringComparison.Ordinal)) {
        continue;
      }

      parsedArguments[i] = parsedArguments[i].Replace("{{URL}}", url, StringComparison.Ordinal);
      hasUrlPlaceholder = true;
    }

    if (!hasUrlPlaceholder) {
      parsedArguments.Add(url);
    }

    return parsedArguments;
  }

  private static List<string> SplitArguments(string argumentTemplate) {
    var arguments = new List<string>();
    var current = new System.Text.StringBuilder();
    char? quoteCharacter = null;

    foreach (var character in argumentTemplate) {
      if (quoteCharacter.HasValue) {
        if (character == quoteCharacter.Value) {
          quoteCharacter = null;
        } else {
          current.Append(character);
        }

        continue;
      }

      if (character is '"' or '\'') {
        quoteCharacter = character;
        continue;
      }

      if (char.IsWhiteSpace(character)) {
        if (current.Length == 0) {
          continue;
        }

        arguments.Add(current.ToString());
        current.Clear();
        continue;
      }

      current.Append(character);
    }

    if (current.Length > 0) {
      arguments.Add(current.ToString());
    }

    return arguments;
  }

  private static bool IsDangerousPlayerArgument(string argument) {
    var dangerous = new[] {"--input-terminal", "--terminal", "--script", "--load-scripts"};
    return dangerous.Any(d =>
      argument.Equals(d, StringComparison.OrdinalIgnoreCase) ||
      argument.StartsWith($"{d}=", StringComparison.OrdinalIgnoreCase));
  }

  private static string FormatArgumentsForLog(IEnumerable<string> arguments) {
    return string.Join(" ", arguments.Select(QuoteArgument));
  }

  private void StopTrackedPlayer(string executableKey) {
    if (!ActivePlayersByExecutable.TryGetValue(executableKey, out var trackedProcess)) {
      return;
    }

    if (!TryGetOwnedProcess(trackedProcess, out var process)) {
      ActivePlayersByExecutable.TryRemove(executableKey, out _);
      return;
    }

    try {
      if (!process.HasExited) {
        logger.LogInformation("Closing existing player process {ProcessId} started by this app", process.Id);
        process.Kill(true);
        process.WaitForExit(5000);
      }
    } catch (Exception ex) {
      logger.LogWarning(ex, "Failed to close previously started player process {ProcessId}", trackedProcess.ProcessId);
    } finally {
      process.Dispose();
      ActivePlayersByExecutable.TryRemove(executableKey, out _);
    }
  }

  private static bool TryGetOwnedProcess(TrackedPlayerProcess trackedProcess, out Process process) {
    process = null!;
    try {
      process = Process.GetProcessById(trackedProcess.ProcessId);
      var startTimeUtc = process.StartTime.ToUniversalTime();
      if (startTimeUtc != trackedProcess.StartTimeUtc) {
        process.Dispose();
        return false;
      }

      return true;
    } catch {
      return false;
    }
  }

  private static string NormalizeExecutablePath(string executablePath) {
    try {
      return Path.GetFullPath(executablePath);
    } catch {
      return executablePath;
    }
  }

  private sealed record TrackedPlayerProcess(int ProcessId, DateTime StartTimeUtc);
}
