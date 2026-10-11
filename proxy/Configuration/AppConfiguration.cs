namespace Tvnoms.Proxy.Configuration;

/// <summary>
/// Strongly-typed configuration for application settings
/// </summary>
public class AppConfiguration {
  public const string SectionName = "App";
  public const string DefaultProductionWebUiUrl = "https://tvnoms.com";
  public const string DefaultDevelopmentWebUiUrl = "https://tvnoms.dev.fergl.ie:3000";

  public NetworkingConfiguration Networking { get; set; } = new();
  public VideoPlayerConfiguration VideoPlayer { get; set; } = new();
  public RecordingsConfiguration Recordings { get; set; } = new();
  public List<string> AllowedCorsOrigins { get; set; } = new();

  /// <summary>
  /// Where the tray's "Open web UI" command points. No compiled-in default — the correct value
  /// depends on the ASP.NET Core environment, so Program.cs computes it before this binds.
  /// </summary>
  public string? WebUiUrl { get; set; }
}

public class NetworkingConfiguration {
  public int Port { get; set; }
}

public class VideoPlayerConfiguration {
  public string? MediaPlayerPath { get; set; }
  public string? MediaPlayerArguments { get; set; }
}

public class RecordingsConfiguration {
  private static readonly string DefaultRecordingsPath = GetDefaultRecordingsPath();

  private static string GetDefaultRecordingsPath() {
    // For Windows Services running as system account, use CommonApplicationData
    // For regular user applications, use MyDocuments
    if (OperatingSystem.IsWindows() &&
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("USERPROFILE"))) {
      // Running as Windows Service (LOCAL SYSTEM)
      return System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Tvnoms",
        "Recordings");
    }

    // Regular user context
    return System.IO.Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
      "TvnomsRecordings");
  }

  public string Path { get; set; } = DefaultRecordingsPath;
  public int MinDurationMinutes { get; set; } = 1;
  public int MaxDurationMinutes { get; set; } = 600;
}

/// <summary>
/// CORS configuration
/// </summary>
public class CorsConfiguration {
  public const string SectionName = "Cors";

  public List<string> AllowedOrigins { get; set; } = new();
}
