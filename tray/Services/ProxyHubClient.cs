using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Tvnoms.Tray.Models;

namespace Tvnoms.Tray.Services;

public enum HubConnectionPhase {
  Connecting,
  Connected,
  Disconnected,
}

/// <summary>Wraps the SignalR connection to tvnoms-proxy's ProxyStatusHub. Re-resolves the
/// proxy's base URL on every connection attempt (see <see cref="ProxyDiscovery"/>), so a proxy
/// restart on a different port is picked up without restarting the tray.
///
/// HubConnection's own WithAutomaticReconnect only covers a connection dropping *after* it
/// connects — it does not retry an initial failed connect (proxy not started yet). This class
/// wraps both cases in one outer retry loop using the same backoff schedule, so "proxy isn't up
/// yet" and "proxy dropped and gave up reconnecting" behave identically from the caller's side.</summary>
public class ProxyHubClient : IAsyncDisposable {
  private static readonly TimeSpan[] BackoffSchedule = [
    TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
    TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30),
  ];

  private CancellationTokenSource? _loopCts;
  private Task? _loopTask;
  private TaskCompletionSource _retryNow = NewRetrySignal();

  public event Action<RecordingChangedEventDto>? RecordingChanged;
  public event Action<RecordingProgressEventDto>? RecordingProgress;
  public event Action<HubConnectionPhase>? PhaseChanged;

  /// <summary>Raised after every fresh connect and every reconnect. The caller should re-fetch
  /// GET /recordings here, since events may have been missed while disconnected.</summary>
  public event Func<Task>? Reconciling;

  /// <summary>Cuts short the current backoff wait and restarts the schedule from the beginning -
  /// used right after the tray starts the proxy itself, so the icon doesn't sit on "disconnected"
  /// for up to the longest backoff step once it's actually up.</summary>
  public void RetryNow() => _retryNow.TrySetResult();

  private static TaskCompletionSource NewRetrySignal() =>
    new(TaskCreationOptions.RunContinuationsAsynchronously);

  public void Start() {
    _loopCts = new CancellationTokenSource();
    _loopTask = ConnectLoopAsync(_loopCts.Token);
  }

  private async Task ConnectLoopAsync(CancellationToken ct) {
    var attempt = 0;
    while (!ct.IsCancellationRequested) {
      PhaseChanged?.Invoke(HubConnectionPhase.Connecting);

      var endpoints = ProxyDiscovery.Resolve();
      var connection = BuildConnection(endpoints.BaseUrl);
      var closedTcs = new TaskCompletionSource();
      connection.Closed += _ => {
        closedTcs.TrySetResult();
        return Task.CompletedTask;
      };

      var canceled = false;
      try {
        await connection.StartAsync(ct);
        attempt = 0;
        PhaseChanged?.Invoke(HubConnectionPhase.Connected);
        if (Reconciling is not null) {
          await Reconciling.Invoke();
        }

        // Blocks until the connection closes permanently (automatic reconnect exhausted, or
        // never started because it wasn't enabled). Brief drops are handled by
        // WithAutomaticReconnect below without ever reaching here.
        await closedTcs.Task.WaitAsync(ct);
      } catch (OperationCanceledException) {
        canceled = true;
      } catch {
        // Initial connect failed outright — fall through to the backoff/retry below.
      } finally {
        PhaseChanged?.Invoke(HubConnectionPhase.Disconnected);
        await connection.DisposeAsync();
      }

      if (canceled) {
        break;
      }

      var delay = BackoffSchedule[Math.Min(attempt, BackoffSchedule.Length - 1)];
      attempt++;
      if (delay > TimeSpan.Zero) {
        var retryNow = _retryNow = NewRetrySignal();
        try {
          var woken = await Task.WhenAny(Task.Delay(delay, ct), retryNow.Task);
          await woken; // surfaces cancellation of the delay
          if (woken == retryNow.Task) {
            attempt = 0;
          }
        } catch (OperationCanceledException) {
          break;
        }
      }
    }
  }

  private HubConnection BuildConnection(string baseUrl) {
    var connection = new HubConnectionBuilder()
      .WithUrl($"{baseUrl}/hubs/proxyStatus")
      .AddJsonProtocol(options => options.PayloadSerializerOptions.PropertyNameCaseInsensitive = true)
      .WithAutomaticReconnect(BackoffSchedule)
      .Build();

    connection.On<RecordingChangedEventDto>("RecordingChanged", e => RecordingChanged?.Invoke(e));
    connection.On<RecordingProgressEventDto>("RecordingProgress", e => RecordingProgress?.Invoke(e));

    connection.Reconnecting += _ => {
      PhaseChanged?.Invoke(HubConnectionPhase.Connecting);
      return Task.CompletedTask;
    };
    connection.Reconnected += async _ => {
      PhaseChanged?.Invoke(HubConnectionPhase.Connected);
      if (Reconciling is not null) {
        await Reconciling.Invoke();
      }
    };

    return connection;
  }

  public async ValueTask DisposeAsync() {
    if (_loopCts is null) {
      return;
    }
    await _loopCts.CancelAsync();
    if (_loopTask is not null) {
      try {
        await _loopTask;
      } catch {
        // Loop already handles/swallows its own exceptions; nothing more to do on shutdown.
      }
    }
    _loopCts.Dispose();
  }
}
