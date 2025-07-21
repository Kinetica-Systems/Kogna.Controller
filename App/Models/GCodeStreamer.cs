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
using KognaServer.ViewModels;
using KognaServer.Views;
using Avalonia.Controls.Shapes;
using System.Linq;
using Avalonia.Controls.Documents;


namespace KognaServer.Server
{
    public class GCodeStreamer : IDisposable
    {

        private readonly CancellationTokenSource? _cts = new()!;
        private readonly StringBuilder? _consoleBuffer = new()!;
        public TextDocument Document { get; } = null!;
        private static readonly String[] bufferedLines = ["line"];
        private readonly StringBuilder _commandBuffer = new();


        public GCodeStreamer()
        {
            BufferCommandFile();
            _cts!.Dispose();
        }

        public void Dispose()
        {
            _cts!.Cancel();
        }


        public void BufferCommandFile()
        {
            
            
            var stream = new MemoryStream(Encoding.Default.GetBytes(Document.Text));
            string _commandBuffer = stream?.ToString()!;
            var newLines = _commandBuffer!.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var l in newLines)
            {
                bufferedLines[int.Parse(l)] = l;
            }

            TerminalPrint();

        }


        private void TerminalPrint()
        {
            for (int i = 0; i < Document.LineCount; i++)
            {
                string printLine = bufferedLines[i];
                Console.WriteLine(printLine);
            }
        }

        /*
                private void EnqueueConsole(string line)
                {
                    lock (_consoleBuffer!)
                        _consoleBuffer.Append(line);
                }
        */

    }

}