using System.Text.RegularExpressions;
using Tvnoms.Proxy.Configuration;

namespace Tvnoms.Proxy.Services;

public interface ILogService {
  Task<List<LogEntry>> GetLogsAsync(int? limit = null, string? level = null, DateTime? startDate = null, DateTime? endDate = null);
  Task<List<string>> GetLogFilesAsync();
  string GetLogDirectory();
}

public class LogService : ILogService {
  private readonly ILogger<LogService> _logger;
  private readonly string _logDirectory;

  // Regex to parse Serilog log format: "yyyy-MM-dd HH:mm:ss.fff zzz [LEVEL] Message"
  private static readonly Regex LogLineRegex = new(
    @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[([A-Z]{3})\] (.*)$",
    RegexOptions.Compiled);

  public LogService(ILogger<LogService> logger) {
    _logger = logger;
    _logDirectory = AppPaths.LogsDirectory;
  }

  public string GetLogDirectory() => _logDirectory;

  public Task<List<string>> GetLogFilesAsync() {
    try {
      if (!Directory.Exists(_logDirectory)) {
        return Task.FromResult(new List<string>());
      }

      var files = Directory.GetFiles(_logDirectory, "applog-*.txt")
        .OrderByDescending(f => f)
        .ToList();

      return Task.FromResult(files.Select(Path.GetFileName).Where(f => f != null).Cast<string>().ToList());
    } catch (Exception ex) {
      _logger.LogError(ex, "Error getting log files from directory: {LogDirectory}", _logDirectory);
      return Task.FromResult(new List<string>());
    }
  }

  public async Task<List<LogEntry>> GetLogsAsync(
    int? limit = null,
    string? level = null,
    DateTime? startDate = null,
    DateTime? endDate = null) {
    var logs = new List<LogEntry>();

    try {
      if (!Directory.Exists(_logDirectory)) {
        _logger.LogWarning("Log directory does not exist: {LogDirectory}", _logDirectory);
        return logs;
      }

      var logFiles = Directory.GetFiles(_logDirectory, "applog-*.txt")
        .OrderByDescending(f => f)
        .ToList();

      foreach (var logFile in logFiles) {
        try {
          var lines = await File.ReadAllLinesAsync(logFile);
          LogEntry? currentEntry = null;

          foreach (var line in lines) {
            var match = LogLineRegex.Match(line);

            if (match.Success) {
              // Save previous entry if exists
              if (currentEntry != null) {
                if (ShouldIncludeLog(currentEntry, level, startDate, endDate)) {
                  logs.Add(currentEntry);
                }
              }

              // Start new entry
              var timestamp = DateTime.Parse(match.Groups[1].Value);
              var logLevel = match.Groups[2].Value;
              var message = match.Groups[3].Value;

              currentEntry = new LogEntry {
                Timestamp = timestamp,
                Level = logLevel,
                Message = message,
                FileName = Path.GetFileName(logFile)
              };
            } else if (currentEntry != null && !string.IsNullOrWhiteSpace(line)) {
              // Multi-line log entry (e.g., exception stack trace)
              currentEntry.Message += Environment.NewLine + line;
            }
          }

          // Don't forget the last entry
          if (currentEntry != null && ShouldIncludeLog(currentEntry, level, startDate, endDate)) {
            logs.Add(currentEntry);
          }
        } catch (Exception ex) {
          _logger.LogError(ex, "Error reading log file: {LogFile}", logFile);
        }
      }

      // Sort by timestamp descending (most recent first)
      logs = logs.OrderByDescending(l => l.Timestamp).ToList();

      // Apply limit if specified
      if (limit.HasValue && limit.Value > 0) {
        logs = logs.Take(limit.Value).ToList();
      }
    } catch (Exception ex) {
      _logger.LogError(ex, "Error reading logs from directory: {LogDirectory}", _logDirectory);
    }

    return logs;
  }

  private static bool ShouldIncludeLog(LogEntry entry, string? level, DateTime? startDate, DateTime? endDate) {
    if (!string.IsNullOrWhiteSpace(level) && !entry.Level.Equals(level, StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    if (startDate.HasValue && entry.Timestamp < startDate.Value) {
      return false;
    }

    if (endDate.HasValue && entry.Timestamp > endDate.Value) {
      return false;
    }

    return true;
  }
}

public class LogEntry {
  public DateTime Timestamp { get; set; }
  public string Level { get; set; } = string.Empty;
  public string Message { get; set; } = string.Empty;
  public string FileName { get; set; } = string.Empty;
}

