using Microsoft.AspNetCore.Mvc;
using Tvnoms.Proxy.Services;

namespace Tvnoms.Proxy.Endpoints;

public static class LogEndpoints {
  public static void RegisterLogEndpoints(this IEndpointRouteBuilder app) {
    var endpoints = app.MapGroup("/logs");

    // Get logs with optional filters
    endpoints.MapGet("", async (
      [FromServices] ILogService logService,
      [FromQuery] int? limit,
      [FromQuery] string? level,
      [FromQuery] DateTime? startDate,
      [FromQuery] DateTime? endDate) => {
      var logs = await logService.GetLogsAsync(limit, level, startDate, endDate);
      return Results.Ok(new {
        count = logs.Count,
        logs
      });
    });

    // Get list of log files
    endpoints.MapGet("/files", async ([FromServices] ILogService logService) => {
      var files = await logService.GetLogFilesAsync();
      return Results.Ok(new {
        count = files.Count,
        files,
        directory = logService.GetLogDirectory()
      });
    });

    // Get logs from a specific file
    endpoints.MapGet("/files/{fileName}", async (
      string fileName,
      [FromServices] ILogService logService,
      [FromQuery] int? limit,
      [FromQuery] string? level) => {
      // Validate filename to prevent directory traversal
      if (fileName.Contains("..") || fileName.Contains("/") || fileName.Contains("\\")) {
        return Results.BadRequest(new {error = "Invalid filename"});
      }

      var logDirectory = logService.GetLogDirectory();
      var filePath = Path.Combine(logDirectory, fileName);

      if (!File.Exists(filePath)) {
        return Results.NotFound(new {error = "Log file not found"});
      }

      var logs = new List<LogEntry>();
      try {
        var lines = await File.ReadAllLinesAsync(filePath);
        LogEntry? currentEntry = null;

        var logLineRegex = new System.Text.RegularExpressions.Regex(
          @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[([A-Z]{3})\] (.*)$",
          System.Text.RegularExpressions.RegexOptions.Compiled);

        foreach (var line in lines) {
          var match = logLineRegex.Match(line);

          if (match.Success) {
            // Save previous entry if exists
            if (currentEntry != null) {
              if (string.IsNullOrWhiteSpace(level) ||
                  currentEntry.Level.Equals(level, StringComparison.OrdinalIgnoreCase)) {
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
              FileName = fileName
            };
          } else if (currentEntry != null && !string.IsNullOrWhiteSpace(line)) {
            // Multi-line log entry
            currentEntry.Message += Environment.NewLine + line;
          }
        }

        // Don't forget the last entry
        if (currentEntry != null) {
          if (string.IsNullOrWhiteSpace(level) ||
              currentEntry.Level.Equals(level, StringComparison.OrdinalIgnoreCase)) {
            logs.Add(currentEntry);
          }
        }

        // Sort by timestamp descending
        logs = logs.OrderByDescending(l => l.Timestamp).ToList();

        // Apply limit if specified
        if (limit.HasValue && limit.Value > 0) {
          logs = logs.Take(limit.Value).ToList();
        }

        return Results.Ok(new {
          fileName,
          count = logs.Count,
          logs
        });
      } catch (Exception ex) {
        return Results.Problem(
          detail: ex.Message,
          statusCode: 500,
          title: "Error reading log file");
      }
    });

    // Get log statistics
    endpoints.MapGet("/stats", async ([FromServices] ILogService logService) => {
      var logs = await logService.GetLogsAsync();

      var stats = new {
        totalLogs = logs.Count,
        levelCounts = logs.GroupBy(l => l.Level)
          .Select(g => new {level = g.Key, count = g.Count()})
          .OrderByDescending(x => x.count)
          .ToList(),
        oldestLog = logs.OrderBy(l => l.Timestamp).FirstOrDefault()?.Timestamp,
        newestLog = logs.OrderByDescending(l => l.Timestamp).FirstOrDefault()?.Timestamp,
        logDirectory = logService.GetLogDirectory()
      };

      return Results.Ok(stats);
    });
  }
}
