using CrystalQuartz.AspNetCore;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Quartz;
using Serilog;
using Tvnoms.Proxy.Configuration;
using Tvnoms.Proxy.Data;
using Tvnoms.Proxy.Endpoints;
using Tvnoms.Proxy.Hubs;
using Tvnoms.Proxy.Models;
using Tvnoms.Proxy.Services;
using Tvnoms.Proxy.Services.Jobs;
using System.Text.Json;

// Handle --version flag
if (args.Contains("--version")) {
  Console.WriteLine(VersionHelper.GetVersion());
  return;
}

// Refuse to run as root on Linux — this is a per-user service and must never
// touch files as uid 0.
if (OperatingSystem.IsLinux() && Environment.UserName == "root") {
  Console.Error.WriteLine("tvnoms-proxy must not be run as root. Use 'systemctl --user' or run as your desktop user.");
  return;
}

// Handle Velopack events first (Windows only)
if (OperatingSystem.IsWindows()) {
  UpdateManager.HandleVelopackEvents();
}

Directory.CreateDirectory(AppPaths.AppDataDirectory);
Directory.CreateDirectory(AppPaths.LogsDirectory);

// Seed appsettings.json into the user config dir on first run, so the user has
// a writable copy they can tweak. Source order: a sibling appsettings.json next
// to the binary (dev / publish output), then /usr/share/tvnoms-proxy/appsettings.json.example
// (system package install).
if (!File.Exists(AppPaths.AppSettingsPath)) {
  var seedCandidates = new[] {
    Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
    "/usr/share/tvnoms-proxy/appsettings.json.example",
  };
  foreach (var seed in seedCandidates) {
    if (File.Exists(seed)) {
      File.Copy(seed, AppPaths.AppSettingsPath);
      break;
    }
  }
}

var builder = WebApplication.CreateBuilder(args);

// Load appsettings.json from the user config dir, not the binary's working directory.
// This is what lets us ship the binary in /usr/bin and keep all user state in $HOME.
builder.Configuration.Sources.Clear();
builder.Configuration
  .AddJsonFile(AppPaths.AppSettingsPath, optional: true, reloadOnChange: true)
  .AddEnvironmentVariables()
  .AddCommandLine(args);

// Set the log path for Serilog
builder.Configuration["Serilog:WriteTo:1:Args:path"] = Path.Combine(AppPaths.LogsDirectory, "applog-.txt");

// appsettings.Production.json / appsettings.Development.json are never loaded (see the
// AddJsonFile call above — only the seeded user copy of the single base appsettings.json is), so
// the environment-appropriate default has to be computed here rather than via file overlays.
builder.Configuration["App:WebUiUrl"] ??= builder.Environment.IsDevelopment()
  ? AppConfiguration.DefaultDevelopmentWebUiUrl
  : AppConfiguration.DefaultProductionWebUiUrl;

builder.Services.Configure<AppConfiguration>(builder.Configuration.GetSection(AppConfiguration.SectionName));
builder.Services.Configure<CorsConfiguration>(builder.Configuration.GetSection(CorsConfiguration.SectionName));

builder.Services.AddDatabase();
builder.Services.AddMigrations();

if (OperatingSystem.IsLinux()) {
  builder.Host.UseSystemd();
}

if (OperatingSystem.IsWindows()) {
  builder.Host.UseWindowsService();
}

builder.Host.UseSerilog((context, configuration) =>
  configuration.ReadFrom.Configuration(context.Configuration));

var connectionString = DatabaseServiceExtensions.GetConfigurationDbConnectionString();

builder.Services.AddJobs(connectionString);

var corsConfig =
  builder
    .Configuration
    .GetSection(CorsConfiguration.SectionName).Get<CorsConfiguration>()
  ?? new CorsConfiguration();

builder.Services.AddCors(options => {
  options.AddPolicy(name: "WebFrontend", policy => {
    policy.WithOrigins(corsConfig.AllowedOrigins.ToArray())
      .AllowAnyHeader()
      .AllowAnyMethod()
      .AllowCredentials();
  });
});

builder.Services.AddHttpClient("StreamPassthrough", c => {
    c.Timeout = Timeout.InfiniteTimeSpan;
    c.DefaultRequestHeaders.UserAgent.ParseAdd("mpv/0.38.0");
  })
  .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler {
    AllowAutoRedirect = false,
    UseCookies = true,
    AutomaticDecompression = System.Net.DecompressionMethods.None,
  })
  .AddResilienceHandler("stream-retry", pipeline => {
    // NOTE: No Polly timeout here — a timeout at the HttpClient handler level would also cancel
    // body streaming after headers are received. The header-only timeout is applied manually
    // with a linked CancellationTokenSource inside FetchWithRedirectsAsync.

    // Retry up to 2 extra times (3 attempts total) on transient failures.
    pipeline.AddRetry(new HttpRetryStrategyOptions {
      MaxRetryAttempts = 2,
      Delay = TimeSpan.FromMilliseconds(250),
      BackoffType = DelayBackoffType.Linear,
      ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
        .Handle<HttpRequestException>()
        .HandleResult(r =>
          r.StatusCode is System.Net.HttpStatusCode.NotFound
            or System.Net.HttpStatusCode.RequestTimeout
            or System.Net.HttpStatusCode.TooManyRequests
            || (int)r.StatusCode >= 500),
    });
  });

builder.Services.AddScoped<IVideoPlayerService, VideoPlayerService>();
builder.Services.AddScoped<IRecordingService, RecordingService>();
builder.Services.AddScoped<ILogService, LogService>();
builder.Services.AddScoped<IRecordingBounds, RecordingBounds>();

// Register validators
builder.Services.AddRecordVmValidator();
builder.Services.AddUpdateRecordingVmValidator();
builder.Services.AddSettingsVmValidator();

// Register update manager (Windows only)
if (OperatingSystem.IsWindows()) {
  builder.Services.AddSingleton<UpdateManager>();
}

// Casing is pinned rather than inherited so hub payloads match what the REST endpoints
// already produce, and the web can share one set of models across both.
builder.Services.AddSignalR()
  .AddJsonProtocol(options =>
    options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

builder.Services.AddSingleton<IRecordingNotifier, RecordingNotifier>();

// Configure server URLs using AppConfiguration (from appsettings.json)
var appConfig = builder.Configuration.GetSection(AppConfiguration.SectionName).Get<AppConfiguration>()
  ?? new AppConfiguration();
var port = appConfig.Networking.Port;
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

app.MigrateDatabase();

// Nothing is capturing yet, so anything still marked "recording" died with a previous run
using (var startupScope = app.Services.CreateScope()) {
  var recordingService = startupScope.ServiceProvider.GetRequiredService<IRecordingService>();
  await recordingService.ReconcileInterruptedRecordingsAsync();
}

app.UseCors("WebFrontend");

app.MapHub<ProxyStatusHub>("/hubs/proxyStatus");
app.MapGet("/", () => "Hello, Sailor!");
app.MapGet("/ping", () => new {Ping = "pong"});

app.RegisterBrowseEndpoints();
app.RegisterVersionEndpoints();
app.RegisterPlayerEndpoints();
app.RegisterStreamEndpoints();
app.RegisterRecordEndpoints();
app.RegisterSettingsEndpoints();
app.RegisterLogEndpoints();
app.UseCrystalQuartz(() => app
  .Services.GetRequiredService<ISchedulerFactory>()
  .GetScheduler());


// Start update check in background (Windows only)
if (OperatingSystem.IsWindows()) {
  var logger = app.Services.GetRequiredService<ILogger<Program>>();
  _ = Task.Run(async () => {
    await Task.Delay(TimeSpan.FromMinutes(1)); // Wait 1 minute after startup
    var updateManager = app.Services.GetRequiredService<UpdateManager>();

    try {
      if (await updateManager.CheckForUpdatesAsync()) {
        logger.LogInformation("Update available. Downloading...");

        if (await updateManager.DownloadAndInstallUpdatesAsync()) {
          logger.LogInformation("Update installed. Application will restart");
          // Velopack will automatically restart the app
        }
      }
    } catch (Exception ex) {
      logger.LogError(ex, "Error during update check");
    }
  });
}

app.Run();
