using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;


namespace KognaServer.Views
{
    public partial class DroView : UserControl
    {
        public DroView()
        {
            InitializeComponent();
        }
    private void InitializeComponent()
            => AvaloniaXamlLoader.Load(this);
    }
}