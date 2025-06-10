using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KognaServer.ViewModels
{
    public partial class ConnectionViewModel : ObservableObject
    {
        // --- IPC client fields ---
        private readonly TcpClient    _ipcClient;
        private readonly StreamReader _ipcReader;
        private readonly StreamWriter _ipcWriter;
        private readonly CancellationTokenSource _cts = new();

        [ObservableProperty]
        private bool _isConnected;

        public string ButtonConnectionStatus => IsConnected ? "Connected" : "Disconnected";
        public IBrush ButtonBrush        => IsConnected ? Brushes.Green : Brushes.Red;

        public ConnectionViewModel()
        {
            // 1) Establish IPC socket
            try
            {
                _ipcClient = new TcpClient("localhost", 5000);
                var stream = _ipcClient.GetStream();
                _ipcReader = new StreamReader(stream, Encoding.UTF8);
                _ipcWriter = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            }
            catch
            {
                // unable to connect to IPC server
                // fall back to harmless, never-null stubs
                _ipcClient = new TcpClient();                  // not connected
                _ipcReader = new StreamReader(Stream.Null);        
                _ipcWriter = new StreamWriter(Stream.Null){AutoFlush=true};
                _isConnected = false;
            }

            // 2) Start polling the KognaServer connection state
            _ = PollConnectionLoopAsync(_cts.Token);
        }

        // 3) Async command for "Connect" button
        [RelayCommand]
        public async Task ConnectAsync()
        {
            await SendCommandAsync("Start");
        }

        // 4) (Optional) you could add a DisconnectAsync similarly:
        // [RelayCommand]
        // public async Task DisconnectAsync()
        // {
        //     await SendCommandAsync("Stop");
        // }

        // 5) Periodically ask the server if it's connected
        private async Task PollConnectionLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var resp = await SendCommandAsync("IsConnected");
                if (bool.TryParse(resp, out var connected))
                {
                    // marshal back onto UI thread
                    Dispatcher.UIThread.Post(() =>
                    {
                        IsConnected = connected;
                        OnPropertyChanged(nameof(ButtonConnectionStatus));
                        OnPropertyChanged(nameof(ButtonBrush));
                    });
                }
                await Task.Delay(1000, ct);
            }
        }

        // 6) Core JSON send/receive
        private async Task<string> SendCommandAsync(string cmd, string[] args = null!)
        {
            if (_ipcWriter == null) return "false";

            var req = new IpcRequest {
                Command = cmd,
                Args    = args ?? Array.Empty<string>()
            };

            try
            {
                await _ipcWriter.WriteLineAsync(JsonConvert.SerializeObject(req));
                var line = await _ipcReader.ReadLineAsync();
                if (line == null) return "false";

                var resp = JsonConvert.DeserializeObject<IpcResponse>(line);
                if (resp == null) return "false";

                return resp.Status == "OK" ? resp.Result : "false";
            }
            catch
            {
                return "false";
            }
        }

        // 7) DTOs for JSON protocol
        private class IpcRequest
        {
            public string   Command { get; set; } = "";
            public string[] Args    { get; set; } = Array.Empty<string>();
        }

        private class IpcResponse
        {
            public string Status { get; set; } = "";
            public string Result { get; set; } = "";
            public string Error  { get; set; } = "";
        }
    }
}
