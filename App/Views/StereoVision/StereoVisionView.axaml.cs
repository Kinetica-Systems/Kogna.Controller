using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KognaServer.ViewModels;

namespace KognaServer.Views.StereoVision
{
    public partial class StereoVisionView : UserControl
    {
        public StereoVisionView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
