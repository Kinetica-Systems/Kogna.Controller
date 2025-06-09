using Avalonia.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;  // at top of file
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KognaServer.Server.KognaServer;  // for KognaServerMain
using KognaServer.ViewModels;
using KognaServer.Views;


namespace KognaServer.ViewModels
{
    public partial class TerminalViewModel : ObservableObject
    {
        private readonly KognaServerMain _server;
        private readonly CancellationTokenSource ct = new();

        [ObservableProperty]
        private string _inputText = "";

        [ObservableProperty]
        private string _consoleText = "";
        

        public TerminalViewModel(KognaServerMain server)
        {
            _server = server;
            ConsoleText = $"Connected to device\n";

            // optional: subscribe to unsolicited console lines
            _server.ConsoleOutput += OnConsoleLine;
            // optional: kick off service‐console loop (if you need streaming)
            Task.Run(async () =>
            {
                while (!ct.Token.IsCancellationRequested)
                {
                    _server._io.ServiceConsole();
                    await Task.Delay(200, ct.Token);
                }
            }, ct.Token);
        }

        [RelayCommand]
        public void SendCommand()
        {
            if (string.IsNullOrWhiteSpace(InputText))
                return;

            // echo the command
            ConsoleText += $"> {InputText}\n";

            // *directly* invoke the server’s SendCommand helper
            var resp = _server.SendCommand(InputText.Trim(), board: 1);
            ConsoleText += resp + "\n";

            InputText = "";
        }

        private void OnConsoleLine(string line)
        {
            Dispatcher.UIThread.Post(() => ConsoleText += line + "\n");
        }
    }
}
