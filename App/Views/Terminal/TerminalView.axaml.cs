using Avalonia;
using Avalonia.Controls;
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

            // once the view-model is assigned…

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
    }
}
