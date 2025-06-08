using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Threading.Tasks;
using Avalonia.Threading;
using KognaServer.Server.KognaServer;
using System.Runtime.CompilerServices;

namespace KognaServer.ViewModels
{
    public class TerminalViewModel : INotifyPropertyChanged
    {
        readonly KognaIO _io;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<string> OutputLines { get; } = new();

        private string _inputText = "";
        public string InputText
        {
            get => _inputText;
            set
            {
                if (_inputText != value)
                {
                    _inputText = value;
                    PropertyChanged?.Invoke( this, new PropertyChangedEventArgs(nameof(InputText)));
                    (_sendCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private ICommand _sendCommand;
        public ICommand SendCommand => _sendCommand;

        public TerminalViewModel(string hostOrUsb, int port = 2000)
        {
                    _io = new KognaIO(hostOrUsb, port);
                    int code = _io.Connect();
                    if (code != KognaIO.KOGNA_OK)
                        Append($"ERROR connecting: {_io.ErrMsg}");
                    else
                        Append($"Connected: {_io.USBLocation()}");

            // 2) setup your Send button
            _sendCommand = new DelegateCommand(OnSend, () => !string.IsNullOrWhiteSpace(InputText));
        }

        private void OnSend()
{
    var cmd = InputText.Trim();
    Append($"> {cmd}");
    InputText = string.Empty;

    // figure out the exact string to send over TCP
    string wire;
    if (cmd.Equals("version", StringComparison.OrdinalIgnoreCase) ||
        cmd.Equals("firmwareversion", StringComparison.OrdinalIgnoreCase))
    {
        wire = "Version";         // what the device actually expects
    }
    else if (cmd.Equals("location", StringComparison.OrdinalIgnoreCase))
    {
        wire = "USBLocation";     // or maybe "Location", depending on your firmware
    }
    else
    {
        wire = cmd;               // send everything else verbatim
    }

    Task.Run(() =>
    {
        try
        {
            string toSend = wire;
            // board=0; you may need to append "\n" or "\0" depending on your firmware
            int rc = _io.WriteLineReadLine(0, toSend, out var response);
            var reply = rc == KognaIO.KOGNA_OK
                        ? response
                        : $"ERROR ({rc}): {response}";
            Dispatcher.UIThread.Post(() => Append(reply));
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => Append($"EXCEPTION: {ex.Message}"));
        }
    });
}



        private void Append(string line)
        {
            Dispatcher.UIThread.Post(() =>
            {
                OutputLines.Add(line);
            });
        }



        // Simple ICommand implementation for sync/async operations
        private class DelegateCommand : ICommand
        {
            readonly Action _exec; readonly Func<bool>? _can;
            public event EventHandler? CanExecuteChanged;
            public DelegateCommand(Action e, Func<bool>? c = null) { _exec=e; _can=c; }
            public bool CanExecute(object? _) => _can?.Invoke() ?? true;
            public void Execute(object? _) => _exec();
            public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this,EventArgs.Empty);
        }
    }
}
