using Microsoft.AspNetCore.SignalR;
using Tvnoms.Proxy.Data.Models;
using Tvnoms.Proxy.Hubs;
using Tvnoms.Proxy.Models;

namespace Tvnoms.Proxy.Services;

public class RecordingNotifier : IRecordingNotifier {
  /// <summary>
  /// A wedged client must never hold up a recording, and SignalR will happily wait on a full
  /// transport buffer, so every push gets a ceiling of its own.
  /// </summary>
  private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(2);

  private readonly IHubContext<ProxyStatusHub> _hub;
  private readonly ILogger<RecordingNotifier> _logger;

  public RecordingNotifier(IHubContext<ProxyStatusHub> hub, ILogger<RecordingNotifier> logger) {
    _hub = hub;
    _logger = logger;
  }

  public Task RecordingChangedAsync(Recording recording, string change) =>
    SendAsync("RecordingChanged", new RecordingChangedEvent {
      Id = recording.Id,
      JobId = recording.JobId,
      Title = recording.Title,
      Status = recording.Status,
      Change = change,
    });

  public Task RecordingProgressAsync(Recording recording, TimeSpan captured, TimeSpan elapsed, TimeSpan duration) =>
    SendAsync("RecordingProgress", new RecordingProgressEvent {
      Id = recording.Id,
      JobId = recording.JobId,
      CapturedSeconds = captured.TotalSeconds,
      ElapsedSeconds = elapsed.TotalSeconds,
      DurationSeconds = duration.TotalSeconds,
    });

  /// <summary>
  /// A push is a convenience - the web reconciles by polling regardless - so nothing that happens
  /// in here is allowed to escape and fail the recording that raised it.
  /// </summary>
  private async Task SendAsync(string method, object payload) {
    try {
      using var cts = new CancellationTokenSource(SendTimeout);
      await _hub.Clients.All.SendAsync(method, payload, cts.Token);
    } catch (Exception ex) {
      _logger.LogWarning(ex, "Failed to push {Method}", method);
    }
  }
}
