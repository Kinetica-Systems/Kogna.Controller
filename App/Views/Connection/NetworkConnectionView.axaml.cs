using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace KognaServer.Views.Connection
{
    public partial class NetworkConnectionView : UserControl
    {
        public NetworkConnectionView()
        {
            InitializeComponent();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}
