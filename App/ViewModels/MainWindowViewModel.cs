using KognaServer;
using KognaServer.ViewModels;
using KognaServer.Server.KognaServer;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Net;

namespace KognaServer.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly KognaServerMain _server;
    public TerminalViewModel TerminalViewModel { get; }
    public MainWindowViewModel(KognaServerMain server)
    {
        _server = server;

        TerminalViewModel = new TerminalViewModel("192.168.0.50", 2000);
        // subscribe to the server’s ConnectionChanged event
        _server.ConnectionChanged += connected =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(ButtonBrush));
                OnPropertyChanged(nameof(ButtonConnectionStatus));
            });
        };
    }

    public bool IsConnected
        => _server.IsConnected;

    public IBrush ButtonBrush
        => IsConnected ? Brushes.Green : Brushes.Red;

    public string ButtonConnectionStatus
        => IsConnected ? "Connected" : "Disconnected";

    // If you want buttons to manually connect/disconnect:
    [RelayCommand]
    public void Connect() => _server.Start();

}
