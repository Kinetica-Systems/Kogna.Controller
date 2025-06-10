using Avalonia.Controls;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace KognaServer.Views
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
        }
     public void ReportProgress(double percent)
        {
            Dispatcher.UIThread.Post(() =>
            {
                LoadingBar.IsIndeterminate = false;
                LoadingBar.Value = percent;
            });
        }

    }
}
