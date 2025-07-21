using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using System.Collections.Specialized;
using KognaServer.ViewModels;

namespace KognaServer.Views
{
    public partial class TerminalView : UserControl
    {
        public TerminalView()
        {
            InitializeComponent();

            // Focus the command input when the control is loaded
            this.AttachedToVisualTree += (s, e) =>
            {
                CommandInput.Focus();
            };

            // whenever DataContext changes, look at the new DataContext directly:
            this.DataContextChanged += (s, e) =>
            {
                if (DataContext is TerminalViewModel vm)
                {
                    // hook up your CollectionChanged
                    ((INotifyCollectionChanged)vm.Lines)
                        .CollectionChanged += LinesChanged;
                }
            };
        }

        private void LinesChanged(object? _, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add)
                return;

            // push the scroll into view on the UI thread
            Dispatcher.UIThread.Post(() =>
            {
                // scroll to the absolute bottom
                ConsoleScroll.Offset = new Vector(
                    0,
                    (int)(ConsoleScroll.Extent.Height - ConsoleScroll.Viewport.Height)
                );
            }, DispatcherPriority.Background);
        }

        private void OnCommandKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not TerminalViewModel vm) return;

            switch (e.Key)
            {
                case Key.Enter:
                    // Let the button's command handle this
                    break;
                    
                case Key.Up:
                    vm.NavigateHistory(-1);
                    e.Handled = true;
                    break;
                    
                case Key.Down:
                    vm.NavigateHistory(1);
                    e.Handled = true;
                    break;
                    
                default:
                    // Let other keys be handled normally
                    break;
            }
        }
    }
}
