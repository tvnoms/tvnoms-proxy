using Avalonia.Controls;
using Tvnoms.Tray.ViewModels;

namespace Tvnoms.Tray.Views;

public partial class AutostartChoiceWindow : Window {
  public AutostartChoiceWindow() {
    InitializeComponent();
    DataContextChanged += OnDataContextChanged;
  }

  private void OnDataContextChanged(object? sender, EventArgs e) {
    if (DataContext is AutostartChoiceViewModel vm) {
      vm.CloseRequested += (_, _) => Close();
    }
  }
}
