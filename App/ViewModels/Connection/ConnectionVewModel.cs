using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KognaServer.Server.KognaServer;

namespace KognaServer.ViewModels
{
    public partial class ConnectionViewModel : ObservableObject
    {
        private readonly KognaServerMain _server;

        public ConnectionViewModel(KognaServerMain server)
        {
            _server = server;
            _server.ConnectionChanged += OnConnectionChanged;
            _isConnected = _server.IsConnected;
        }

        [ObservableProperty]
        private bool _isConnected;

        public string ButtonConnectionStatus => IsConnected ? "Connected" : "Disconnected";
        public IBrush ButtonBrush => IsConnected ? Brushes.Green : Brushes.Red;
        public RelayCommand ConnectCommand => new RelayCommand(() => _server.Start());

        private void OnConnectionChanged(bool connected)
        {
            IsConnected = connected;
            OnPropertyChanged(nameof(ButtonConnectionStatus));
            OnPropertyChanged(nameof(ButtonBrush));
        }
    }
}
