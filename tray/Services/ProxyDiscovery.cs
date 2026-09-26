using Microsoft.Extensions.Configuration;

namespace Tvnoms.Tray.Services;

/// <summary>Where tvnoms-proxy actually is right now, resolved fresh on every (re)connect
/// attempt so a proxy restart on a different port is picked up without restarting the tray.</summary>
public record ProxyEndpoints(int Port) {
  public string BaseUrl => $"http://127.0.0.1:{Port}";
}

public static class ProxyDiscovery {
  private const int DefaultPort = 8963;

  // Mirrors Tvnoms.Proxy.Configuration.AppPaths.AppSettingsPath exactly, just pointed at the
  // proxy's own app-data directory instead of the tray's. Reading the proxy's live config here is
  // what lets the tray find it with zero changes to the proxy itself. Only the port is read this
  // way - it's the one thing needed before any HTTP call to the proxy is possible at all. Every
  // other setting (including WebUiUrl) comes from the proxy's real /settings API instead, since
  // that's the DB-backed source of truth, not this seeded-once file.
  private static string ProxyAppSettingsPath => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "tvnoms-proxy", "appsettings.json");

  // Written by the proxy's Velopack OnFirstRun hook (UpdateManager.PersistMode) once the user
  // picks a startup mode - see ProxyServiceControl, which reads this to decide how to start and
  // stop the proxy. Same cross-directory-read trick as ProxyAppSettingsPath above.
  private static string ProxyAutostartModePath => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "tvnoms-proxy", "autostart-mode.txt");

  public static string? ReadProxyAutostartMode() =>
    File.Exists(ProxyAutostartModePath) ? File.ReadAllText(ProxyAutostartModePath).Trim() : null;

  public static ProxyEndpoints Resolve() {
    var port = DefaultPort;

    if (File.Exists(ProxyAppSettingsPath)) {
      var config = new ConfigurationBuilder()
        .AddJsonFile(ProxyAppSettingsPath, optional: true)
        .Build();
      port = config.GetValue<int?>("App:Networking:Port") ?? DefaultPort;
    }

    return new ProxyEndpoints(port);
  }
}
