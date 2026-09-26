using System.Reflection;

namespace Tvnoms.Proxy.Services;

/// <summary>
/// Helper class for retrieving application version information
/// </summary>
public static class VersionHelper {
  /// <summary>
  /// Gets the application version in format "v{major}.{minor}.{build}"
  /// </summary>
  public static string GetVersion() {
    return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "Unknown";
  }
}
