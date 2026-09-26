using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Tvnoms.Tray.ViewModels;

/// <summary>Backs the one-time first-run picker that decides how tvnoms-proxy starts itself.
/// Launched by the proxy's Velopack OnFirstRun hook as a separate, short-lived tray.exe process
/// (see Program's --pick-autostart-mode handling) - the proxy performs the actual install, this
/// only collects the answer by writing it to outputPath for the waiting proxy process to read.</summary>
public partial class AutostartChoiceViewModel : ObservableObject {
  private readonly string _outputPath;

  public event EventHandler? CloseRequested;

  public AutostartChoiceViewModel(string outputPath) {
    _outputPath = outputPath;
  }

  [RelayCommand]
  private void ChooseService() => Choose("Service");

  [RelayCommand]
  private void ChooseScheduledTask() => Choose("ScheduledTask");

  [RelayCommand]
  private void ChooseRunKey() => Choose("RunKey");

  private void Choose(string mode) {
    try {
      File.WriteAllText(_outputPath, mode);
    } catch {
      // The waiting proxy process falls back to a safe default if the file never appears.
    }
    CloseRequested?.Invoke(this, EventArgs.Empty);
  }
}
