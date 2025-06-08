using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using KognaServer.ViewModels;

namespace KognaServer.Views
{
  public partial class TerminalView : UserControl
  {
    public TerminalView()
    {
      InitializeComponent();
    }

    private void OnInputKeyUp(object? sender, KeyEventArgs e)
    {
      if (e.Key == Key.Enter && DataContext is TerminalViewModel vm)
        vm.SendCommand.Execute(null);
    }
  }
}
