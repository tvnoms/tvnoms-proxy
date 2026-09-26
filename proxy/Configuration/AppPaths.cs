namespace Tvnoms.Proxy.Configuration;

public static class AppPaths {
  public const string DirectoryName = "tvnoms-proxy";

  public static string AppDataDirectory => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    DirectoryName);

  public static string AppSettingsPath => Path.Combine(AppDataDirectory, "appsettings.json");

  public static string LogsDirectory => Path.Combine(AppDataDirectory, "logs");

  /// <summary>Which of Service/ScheduledTask/RunKey was chosen at first run (Windows only). Written
  /// before the DI container / settings DB exist, so it can't live in ISettingsRepository - see
  /// UpdateManager.HandleVelopackEvents.</summary>
  public static string AutostartModePath => Path.Combine(AppDataDirectory, "autostart-mode.txt");
}
