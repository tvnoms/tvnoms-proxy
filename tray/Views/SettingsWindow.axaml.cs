using Avalonia.Controls;
using Tvnoms.Tray.ViewModels;

namespace Tvnoms.Tray.Views;

public partial class SettingsWindow : Window {
  public SettingsWindow() {
    InitializeComponent();
    DataContextChanged += OnDataContextChanged;
  }

  private void OnDataContextChanged(object? sender, EventArgs e) {
    if (DataContext is SettingsWindowViewModel vm) {
      vm.CloseRequested += (_, _) => Close();
    }
  }
}
