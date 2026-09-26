using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Tvnoms.Proxy.Data.Models;
using Tvnoms.Proxy.Data.Repositories;
using Tvnoms.Proxy.Services;

namespace Tvnoms.Proxy.Tests;

public class VideoPlayerServiceTests {
  [Fact]
  public async Task PlayFromUrl_SecondCall_ClosesOnlyAppTrackedInstance() {
    if (!OperatingSystem.IsLinux()) {
      return;
    }

    var tempDirectory = Directory.CreateTempSubdirectory("videoplayer-service-tests-");
    var scriptPath = Path.Combine(tempDirectory.FullName, "fake-player.sh");
    var launchLogPath = Path.Combine(tempDirectory.FullName, "launches.log");

    await File.WriteAllTextAsync(scriptPath,
      $"#!/usr/bin/env sh\nprintf '%s|%s\\n' \"$$\" \"$1\" >> '{launchLogPath}'\nsleep 120\n");
    File.SetUnixFileMode(scriptPath,
      UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
      UnixFileMode.GroupRead | UnixFileMode.GroupExecute);

    var repository = new FakeSettingsRepository(new Dictionary<string, string> {
      ["MediaPlayerPath"] = scriptPath,
      ["MediaPlayerArguments"] = string.Empty
    });

    var service = new VideoPlayerService(NullLogger<VideoPlayerService>.Instance, repository);

    using var externallyStartedProcess = Process.Start(new ProcessStartInfo {
      FileName = scriptPath,
      Arguments = "external",
      UseShellExecute = false,
      CreateNoWindow = true
    });

    Assert.NotNull(externallyStartedProcess);
    var externalPid = externallyStartedProcess!.Id;

    var firstPlayResult = await service.PlayFromUrl("stream-1");
    Assert.True(firstPlayResult.Success);
    var firstAppPid = await WaitForLaunchPidAsync(launchLogPath, "stream-1");

    var secondPlayResult = await service.PlayFromUrl("stream-2");
    Assert.True(secondPlayResult.Success);
    var secondAppPid = await WaitForLaunchPidAsync(launchLogPath, "stream-2");

    Assert.True(IsProcessAlive(externalPid));
    Assert.False(IsProcessAlive(firstAppPid));
    Assert.True(IsProcessAlive(secondAppPid));

    KillIfAlive(secondAppPid);
    KillIfAlive(externalPid);
    tempDirectory.Delete(recursive: true);
  }
  
  [Fact]
  public async Task PlayFromUrl_SecondCall_ClosesOnlyAppTrackedInstance_WithCleanup() {
    if (!OperatingSystem.IsLinux()) {
      return;
    }

    var tempDirectory = Directory.CreateTempSubdirectory("videoplayer-service-tests-");
    var secondAppPid = 0;
    var externalPid = 0;
    try {
      var scriptPath = Path.Combine(tempDirectory.FullName, "fake-player.sh");
      var launchLogPath = Path.Combine(tempDirectory.FullName, "launches.log");

      await File.WriteAllTextAsync(scriptPath,
        $"#!/usr/bin/env sh\nprintf '%s|%s\\n' \"$$\" \"$1\" >> '{launchLogPath}'\nsleep 120\n");
      File.SetUnixFileMode(scriptPath,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute);

      var repository = new FakeSettingsRepository(new Dictionary<string, string> {
        ["MediaPlayerPath"] = scriptPath,
        ["MediaPlayerArguments"] = string.Empty
      });

      var service = new VideoPlayerService(NullLogger<VideoPlayerService>.Instance, repository);

      using var externallyStartedProcess = Process.Start(new ProcessStartInfo {
        FileName = scriptPath,
        Arguments = "external",
        UseShellExecute = false,
        CreateNoWindow = true
      });

      Assert.NotNull(externallyStartedProcess);
      externalPid = externallyStartedProcess!.Id;

      var firstPlayResult = await service.PlayFromUrl("stream-1");
      Assert.True(firstPlayResult.Success);
      var firstAppPid = await WaitForLaunchPidAsync(launchLogPath, "stream-1");

      var secondPlayResult = await service.PlayFromUrl("stream-2");
      Assert.True(secondPlayResult.Success);
      secondAppPid = await WaitForLaunchPidAsync(launchLogPath, "stream-2");

      Assert.True(IsProcessAlive(externalPid));
      Assert.False(IsProcessAlive(firstAppPid));
      Assert.True(IsProcessAlive(secondAppPid));
    } finally {
      KillIfAlive(secondAppPid);
      KillIfAlive(externalPid);
      try {
        tempDirectory.Delete(recursive: true);
      } catch {
        // Ignore cleanup errors.
      }
    }
  }

  [Fact]
  public async Task PlayFromUrl_StaleTrackedOwnership_DoesNotCloseExternalProcess() {
    if (!OperatingSystem.IsLinux()) {
      return;
    }

    var tempDirectory = Directory.CreateTempSubdirectory("videoplayer-service-tests-");
    var scriptPath = Path.Combine(tempDirectory.FullName, "fake-player.sh");
    var launchLogPath = Path.Combine(tempDirectory.FullName, "launches.log");

    await File.WriteAllTextAsync(scriptPath,
      $"#!/usr/bin/env sh\nprintf '%s|%s\\n' \"$$\" \"$1\" >> '{launchLogPath}'\nsleep 120\n");
    File.SetUnixFileMode(scriptPath,
      UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
      UnixFileMode.GroupRead | UnixFileMode.GroupExecute);

    var repository = new FakeSettingsRepository(new Dictionary<string, string> {
      ["MediaPlayerPath"] = scriptPath,
      ["MediaPlayerArguments"] = string.Empty
    });

    var service = new VideoPlayerService(NullLogger<VideoPlayerService>.Instance, repository);

    using var externalProcess = Process.Start(new ProcessStartInfo {
      FileName = scriptPath,
      Arguments = "external-stale",
      UseShellExecute = false,
      CreateNoWindow = true
    });

    Assert.NotNull(externalProcess);
    var externalPid = externalProcess!.Id;
    var externalStartTimeUtc = externalProcess.StartTime.ToUniversalTime();

    // Seed stale ownership tracking: same PID, wrong start time.
    SetTrackedPlayerProcessForTests(scriptPath, externalPid, externalStartTimeUtc.AddSeconds(-1));

    var playResult = await service.PlayFromUrl("stream-stale");
    Assert.True(playResult.Success);

    var appPid = await WaitForLaunchPidAsync(launchLogPath, "stream-stale");

    Assert.True(IsProcessAlive(externalPid));
    Assert.True(IsProcessAlive(appPid));

    KillIfAlive(appPid);
    KillIfAlive(externalPid);
    tempDirectory.Delete(recursive: true);
  }

  [Fact]
  public async Task PlayFromUrl_StaleTrackedOwnership_DoesNotCloseExternalProcess_WithCleanup() {
    if (!OperatingSystem.IsLinux()) {
      return;
    }

    var tempDirectory = Directory.CreateTempSubdirectory("videoplayer-service-tests-");
    var appPid = 0;
    var externalPid = 0;
    try {
      var scriptPath = Path.Combine(tempDirectory.FullName, "fake-player.sh");
      var launchLogPath = Path.Combine(tempDirectory.FullName, "launches.log");

      await File.WriteAllTextAsync(scriptPath,
        $"#!/usr/bin/env sh\nprintf '%s|%s\\n' \"$$\" \"$1\" >> '{launchLogPath}'\nsleep 120\n");
      File.SetUnixFileMode(scriptPath,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute);

      var repository = new FakeSettingsRepository(new Dictionary<string, string> {
        ["MediaPlayerPath"] = scriptPath,
        ["MediaPlayerArguments"] = string.Empty
      });

      var service = new VideoPlayerService(NullLogger<VideoPlayerService>.Instance, repository);

      using var externalProcess = Process.Start(new ProcessStartInfo {
        FileName = scriptPath,
        Arguments = "external-stale",
        UseShellExecute = false,
        CreateNoWindow = true
      });

      Assert.NotNull(externalProcess);
      externalPid = externalProcess!.Id;
      var externalStartTimeUtc = externalProcess.StartTime.ToUniversalTime();

      // Seed stale ownership tracking: same PID, wrong start time.
      SetTrackedPlayerProcessForTests(scriptPath, externalPid, externalStartTimeUtc.AddSeconds(-1));

      var playResult = await service.PlayFromUrl("stream-stale");
      Assert.True(playResult.Success);

      appPid = await WaitForLaunchPidAsync(launchLogPath, "stream-stale");

      Assert.True(IsProcessAlive(externalPid));
      Assert.True(IsProcessAlive(appPid));
    } finally {
      KillIfAlive(appPid);
      KillIfAlive(externalPid);
      try {
        tempDirectory.Delete(recursive: true);
      } catch {
        // Ignore cleanup errors.
      }
    }
  }

  [Fact]
  public async Task PlayFromUrl_MultilineArguments_ArePassedAsSeparateArguments() {
    if (!OperatingSystem.IsLinux()) {
      return;
    }

    var tempDirectory = Directory.CreateTempSubdirectory("videoplayer-service-tests-");
    var appPid = 0;
    try {
      var scriptPath = Path.Combine(tempDirectory.FullName, "fake-player.sh");
      var launchLogPath = Path.Combine(tempDirectory.FullName, "launches.log");
      const string url = "https://example.com/stream.ts";

      await File.WriteAllTextAsync(scriptPath,
        $"#!/usr/bin/env sh\nprintf '%s' \"$$\" >> '{launchLogPath}'\nfor arg in \"$@\"; do\n  printf '|%s' \"$arg\" >> '{launchLogPath}'\ndone\nprintf '\\n' >> '{launchLogPath}'\nsleep 120\n");
      File.SetUnixFileMode(scriptPath,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute);

      var repository = new FakeSettingsRepository(new Dictionary<string, string> {
        ["MediaPlayerPath"] = scriptPath,
        ["MediaPlayerArguments"] = "--keep-open=yes\n--geometry=1024x768-0-0\n--ontop"
      });

      var service = new VideoPlayerService(NullLogger<VideoPlayerService>.Instance, repository);

      var playResult = await service.PlayFromUrl(url);
      Assert.True(playResult.Success);

      var launchRecord = await WaitForLaunchRecordAsync(launchLogPath, url);
      appPid = launchRecord.ProcessId;

      Assert.Equal(new[] {
        "--keep-open=yes",
        "--geometry=1024x768-0-0",
        "--ontop",
        url
      }, launchRecord.Arguments);
    } finally {
      KillIfAlive(appPid);
      try {
        tempDirectory.Delete(recursive: true);
      } catch {
        // Ignore cleanup errors.
      }
    }
  }

  private static async Task<int> WaitForLaunchPidAsync(string launchLogPath, string marker) {
    return (await WaitForLaunchRecordAsync(launchLogPath, marker)).ProcessId;
  }

  private static async Task<LaunchRecord> WaitForLaunchRecordAsync(string launchLogPath, string marker) {
    var timeoutAt = DateTime.UtcNow.AddSeconds(8);

    while (DateTime.UtcNow < timeoutAt) {
      if (File.Exists(launchLogPath)) {
        var lines = await File.ReadAllLinesAsync(launchLogPath);
        foreach (var line in lines) {
          var parts = line.Split('|');
          if (parts.Length >= 2 && parts.Skip(1).Any(arg => arg.Contains(marker, StringComparison.Ordinal)) &&
              int.TryParse(parts[0], out var pid)) {
            return new LaunchRecord(pid, parts.Skip(1).ToArray());
          }
        }
      }

      await Task.Delay(100);
    }

    throw new TimeoutException($"Timed out waiting for launch marker '{marker}' in {launchLogPath}");
  }

  private static bool IsProcessAlive(int pid) {
    try {
      using var process = Process.GetProcessById(pid);
      return !process.HasExited;
    } catch {
      return false;
    }
  }

  private static void KillIfAlive(int pid) {
    try {
      using var process = Process.GetProcessById(pid);
      if (!process.HasExited) {
        process.Kill(entireProcessTree: true);
        process.WaitForExit(3000);
      }
    } catch {
      // Ignore cleanup errors.
    }
  }

  private static void SetTrackedPlayerProcessForTests(string executablePath, int processId, DateTime startTimeUtc) {
    var serviceType = typeof(VideoPlayerService);
    var trackerField = serviceType.GetField("ActivePlayersByExecutable",
      BindingFlags.Static | BindingFlags.NonPublic);
    var trackedType = serviceType.GetNestedType("TrackedPlayerProcess", BindingFlags.NonPublic);

    Assert.NotNull(trackerField);
    Assert.NotNull(trackedType);

    var tracker = trackerField!.GetValue(null);
    var trackedEntry = Activator.CreateInstance(trackedType!, processId, startTimeUtc);

    Assert.NotNull(tracker);
    Assert.NotNull(trackedEntry);

    var executableKey = Path.GetFullPath(executablePath);
    var indexer = tracker!.GetType().GetProperty("Item");
    Assert.NotNull(indexer);
    indexer!.SetValue(tracker, trackedEntry, [executableKey]);
  }

  private sealed record LaunchRecord(int ProcessId, string[] Arguments);
}






