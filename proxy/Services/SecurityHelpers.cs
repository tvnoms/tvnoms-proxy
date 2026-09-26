using System.Security;

namespace Tvnoms.Proxy.Services;

/// <summary>
/// Security and validation helpers
/// </summary>
public static class SecurityHelpers {
  /// <summary>
  /// Validate and sanitize file paths to prevent directory traversal
  /// </summary>
  public static string ValidateAndSanitizeFilePath(string basePath, string fileName) {
    if (string.IsNullOrWhiteSpace(basePath)) {
      throw new ArgumentException("Base path cannot be null or empty", nameof(basePath));
    }

    if (string.IsNullOrWhiteSpace(fileName)) {
      throw new ArgumentException("File name cannot be null or empty", nameof(fileName));
    }

    // Remove directory traversal attempts
    var sanitizedFileName = Path.GetFileName(fileName);
    if (string.IsNullOrWhiteSpace(sanitizedFileName)) {
      throw new ArgumentException("Invalid file name", nameof(fileName));
    }

    // Remove potentially dangerous characters
    var invalidChars = Path.GetInvalidFileNameChars();
    foreach (var invalidChar in invalidChars) {
      sanitizedFileName = sanitizedFileName.Replace(invalidChar, '_');
    }

    // Combine paths safely
    var fullPath = Path.Combine(basePath, sanitizedFileName);

    // Ensure the final path is within the base directory
    var fullBasePath = Path.GetFullPath(basePath);
    var fullFilePath = Path.GetFullPath(fullPath);

    if (!fullFilePath.StartsWith(fullBasePath, StringComparison.OrdinalIgnoreCase)) {
      throw new SecurityException("Path traversal attempt detected");
    }

    return fullFilePath;
  }

  /// <summary>
  /// Ensure directory exists and is writable
  /// </summary>
  public static void EnsureDirectoryExistsAndWritable(string directoryPath) {
    if (string.IsNullOrWhiteSpace(directoryPath)) {
      throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));
    }

    try {
      if (!Directory.Exists(directoryPath)) {
        Directory.CreateDirectory(directoryPath);
      }

      // Test write permissions
      var testFile = Path.Combine(directoryPath, $"test_write_{Guid.NewGuid()}.tmp");
      File.WriteAllText(testFile, "test");
      File.Delete(testFile);
    } catch (UnauthorizedAccessException ex) {
      throw new SecurityException($"Directory '{directoryPath}' is not writable", ex);
    } catch (Exception ex) {
      throw new InvalidOperationException($"Cannot access directory '{directoryPath}'", ex);
    }
  }
}
