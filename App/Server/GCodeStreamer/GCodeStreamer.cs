using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
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
using System.Timers;

using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

using System.Collections.ObjectModel;

using KognaServer.Models;
using KognaServer.Server;
using KognaServer.Server.KognaServer;
using KognaServer.ViewModels;
using KognaServer.Views;
using Avalonia.Controls.Shapes;
using System.Linq;
using Avalonia.Controls.Documents;


namespace KognaServer.Server
{
    public class GCodeStreamer : IDisposable
    {
        // --- IPC client fields ---
        private readonly TcpClient _ipcClient;
        private readonly StreamReader _ipcReader;
        private readonly StreamWriter _ipcWriter;
        private readonly CancellationTokenSource _cts = new();
        private bool _isConnected;
        private readonly StringBuilder _consoleBuffer = new();
        private readonly System.Timers.Timer _flushTimer;
        public TextDocument Document { get; }
        private static readonly String[] bufferedLines = ["line"];
        private readonly StringBuilder _commandBuffer = new();


        public GCodeStreamer()
        {

            /* // 1) Establish IPC socket
             try
             {
                 _ipcClient = new TcpClient("localhost", 5000);
                 var stream = _ipcClient.GetStream();
                 _ipcReader = new StreamReader(stream, Encoding.UTF8);
                 _ipcWriter = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                 EnqueueConsole("Streamer IPC socket connected to LocalHost:5000\n");
             }
             catch (Exception ex)
             {
                 EnqueueConsole($"Streamer IPC connection failed: {ex.Message}\n");
                 // fall back to harmless, never-null stubs
                 _ipcClient = new TcpClient();                  // not connected
                 _ipcReader = new StreamReader(Stream.Null);
                 _ipcWriter = new StreamWriter(Stream.Null) { AutoFlush = true };
                 _isConnected = false;
             }
             // 2) Kick off the flush loop
             _flushTimer = new System.Timers.Timer(200);
             _flushTimer.Elapsed += (s, e) => FlushBuffer();
             _flushTimer.AutoReset = true;
             _flushTimer.Start();
 */


            BufferCommandFile();


            _cts.Dispose();

        }

        public void Dispose()
        {
            _cts.Cancel();

        }


        public void BufferCommandFile()
        {
            var _lineCount = Document.LineCount;
            
            var stream = new MemoryStream(Encoding.Default.GetBytes(Document.Text));
            string _commandBuffer = stream?.ToString();
            var newLines = _commandBuffer
                            .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var l in newLines)
            {
                bufferedLines[int.Parse(l)] = l;
            }

            TerminalPrint();



            // await stream.CopyToAsync(writeStream);

        }

        private void TerminalPrint()
        {
            //var _lineCount = Document.LineCount;
            for (int i = 0; i < Document.LineCount; i++)
            {
                string printLine = bufferedLines[i];
                Console.WriteLine(printLine);

            }

          

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

        }


        private void EnqueueConsole(string line)
        {
            lock (_consoleBuffer)
                _consoleBuffer.Append(line);
        }


/*
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
*/
        private string FormatIpcResponse(string raw)   //Format the JSON response from the server to be viewable on the console window
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

        // 7) DTOs for JSON protocol
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






    }

}