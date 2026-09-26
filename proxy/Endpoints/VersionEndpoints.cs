using Tvnoms.Proxy.Services;

namespace Tvnoms.Proxy.Endpoints;

public class VersionInfo {
  public required string Version { get; set; }
  public required string Environment { get; set; }
  public required string OSPlatform { get; set; }
  public required string OSDescription { get; set; }
  public required string OSVersion { get; set; }
  public required string CoreVersion { get; set; }
  public required string RuntimeVersion { get; set; }
}

public static class VersionEndpoints {
  public static void RegisterVersionEndpoints(this IEndpointRouteBuilder app) {
    app.MapGet("/version", () =>
      Results.Ok(new VersionInfo {
        Version = VersionHelper.GetVersion(),
        Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "unknown",
        OSPlatform = Environment.OSVersion.Platform.ToString(),
        OSDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        OSVersion = Environment.OSVersion.ToString(),
        CoreVersion = Environment.Version.ToString(),
        RuntimeVersion = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
      }));
  }
}
