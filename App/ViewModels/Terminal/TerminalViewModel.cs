using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace KognaServer.ViewModels
{
    public partial class TerminalViewModel : ObservableObject
    {
        // --- IPC client fields ---
        private readonly TcpClient _ipcClient;
        private readonly StreamReader _ipcReader;
        private readonly StreamWriter _ipcWriter;

        private readonly CancellationTokenSource _ct = new();

        [ObservableProperty] private string _inputText = "";
        public ObservableCollection<string> Lines { get; } = new();



        // batching buffer & flush
        private readonly StringBuilder _consoleBuffer = new();
        private readonly TimeSpan _flushInterval = TimeSpan.FromMilliseconds(100);
        //private bool _flushing;
        private readonly System.Timers.Timer _flushTimer;

        public TerminalViewModel()
        {
            // 1) Initialize the socket
            try
            {
                _ipcClient = new TcpClient("localhost", 5000);
                var stream = _ipcClient.GetStream();
                _ipcReader = new StreamReader(stream, Encoding.UTF8);
                _ipcWriter = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                EnqueueConsole("🔌 IPC socket connected to 127.0.0.1:5000\n");
            }
            catch (Exception ex)
            {
                EnqueueConsole($" IPC connection failed: {ex.Message}\n");

                // fall back to harmless, never-null stubs
                _ipcClient = new TcpClient();                  
                _ipcReader = new StreamReader(Stream.Null);        
                _ipcWriter = new StreamWriter(Stream.Null){AutoFlush=true};
        
            }

            // 2) Kick off the flush loop
            _flushTimer = new System.Timers.Timer(200);
            _flushTimer.Elapsed += (s, e) => FlushBuffer();
            _flushTimer.AutoReset = true;
            _flushTimer.Start();
        }

        private void FlushBuffer()
        {
            string text;
            lock (_consoleBuffer)
            {
                if (_consoleBuffer.Length == 0)
                    return;
                text = _consoleBuffer.ToString();
                _consoleBuffer.Clear();
            }

            var newLines = text
                 .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            Dispatcher.UIThread.Post(() =>
                    {
                        foreach (var l in newLines)
                        {
                            Lines.Add(l);
                        }

                        // optional: keep only the last N lines
                        const int Max = 100;
                        while (Lines.Count > Max)
                            Lines.RemoveAt(0);
                    }, DispatcherPriority.Background);
        }



        [RelayCommand]
        public async Task SendCommand()
        {
            if (string.IsNullOrWhiteSpace(InputText))
                return;

            // 3) Echo & enqueue user input
            EnqueueConsole($"> {InputText}\n");
            var ipcReq = new IpcRequest
            {
                Command = InputText.Trim(),
                Args = Array.Empty<string>()
            };

            try
            {
                // 4) Send the JSON request
                if (_ipcWriter != null)
                    await _ipcWriter.WriteLineAsync(JsonConvert.SerializeObject(ipcReq));
                else
                    EnqueueConsole("⚠️ No IPC writer available\n");

                // 5) Read one line of response
                var line = await _ipcReader.ReadLineAsync();
                var output = line == null
                    ? "⚠️ No response"
                    : FormatIpcResponse(line);

                EnqueueConsole(output + "\n");
                            }
                            catch (Exception ex)
                            {
                                EnqueueConsole($"⚠️ IPC error: {ex.Message}\n");
                            }
                            finally
                            {
                                InputText = "";
                            }
        }


        private void EnqueueConsole(string line)
        {
            lock (_consoleBuffer)
                _consoleBuffer.Append(line);
        }

        // --- JSON DTOs ---
        private class IpcRequest
        {
            public string Command { get; set; } = "";
            public string[] Args { get; set; } = Array.Empty<string>();
        }

        private class IpcResponse
        {
            public string Status { get; set; } = "";
            public string Result { get; set; } = "";
            public string Error { get; set; } = "";
        }

        private string FormatIpcResponse(string raw)
        {
            try
            {
                // attempt to deserialize into your existing DTO
                var resp = JsonConvert.DeserializeObject<IpcResponse>(raw);
                if (resp == null)
                    return raw;

                // if Status is OK, show the Result; otherwise show the Error
                return resp.Status == "OK"
                    ? resp.Result
                    : $"⚠️ {resp.Status}: {resp.Error}";
            }
            catch (JsonException)
            {
                // not valid JSON? just echo back the raw line
                return raw;
            }
        }
    }
}
