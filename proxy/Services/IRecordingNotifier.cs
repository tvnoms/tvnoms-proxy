using Tvnoms.Proxy.Data.Models;

namespace Tvnoms.Proxy.Services;

/// <summary>
/// Pushes recording activity to connected web clients.
///
/// Note the deliberate absence of a CancellationToken on these methods. The obvious token to
/// hand them is the job's own, and the most important push of all - "your recording was
/// cancelled" - is raised from the handler for that token being cancelled. Accepting one would
/// mean that push silently never went out.
/// </summary>
public interface IRecordingNotifier {
  Task RecordingChangedAsync(Recording recording, string change);

  Task RecordingProgressAsync(Recording recording, TimeSpan captured, TimeSpan elapsed, TimeSpan duration);
}
