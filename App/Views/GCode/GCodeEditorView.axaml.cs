
using Avalonia;
using Avalonia.Media;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Diagnostics;
using Avalonia.Media.Imaging;

using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Utils;
using AvaloniaEdit.Rendering;


using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;

using System;
using System.ComponentModel;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Collections.ObjectModel;
using System.Collections.Generic;

using KognaServer.ViewModels;
using KognaServer.Server;

using System.Reactive.Joins;
using System.Security.Cryptography.X509Certificates;
using Avalonia.Controls.Documents;
using System.Net;

namespace KognaServer.Views
{

    public partial class GCodeEditorView : UserControl
    {

        public string? fileContent { get; set; } = null!;
        public bool? IsModified { get; set; } = null!;
        public TextDocument? Document { get; set; } = null!;
        private readonly ObservableCollection<string> _responses = new();
        private GCodeStreamer? bufferCommandFile { get; set; } = null!;
        private String[] bufferedLines = [];
        private readonly KinematicEngineClient? _client;
        public GCodeEditorView()
        {
            InitializeComponent();
            _client = new KinematicEngineClient("127.0.0.1", 5000);
            ResponseList.ItemsSource = _responses;
            Editor.Background = Brushes.Transparent;
            Editor.Foreground = Brushes.LightGray;
            Editor.ShowLineNumbers = true;
            Editor.Document = new TextDocument();
            Editor.Document.Changed += (_, __) =>
                        {
                            var hasText = Editor.Document.Lines.Any(l => !string.IsNullOrWhiteSpace(Editor.Document.GetText(l.Offset, l.Length)));
                            IsEnabled = hasText;
                        };
        }

        private async void OpenFileButton_Clicked(object sender, RoutedEventArgs args)
        {
            // Get top level from the current control. Alternatively, you can use Window reference instead.
            var topLevel = TopLevel.GetTopLevel(this)!;

            // Start async operation to open the dialog.
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
            {
                Title = "Open Text File",
                //FileTypeFilter = "NGC"
                FileTypeFilter = new FilePickerFileType[] { new("GCode Files") { Patterns = new[] { "*.gcode", "*.txt", "*.nc" }, MimeTypes = new[] { "*/*" } } }

            });

            if (files.Count >= 1)
            {
                // Open reading stream from the first file.
                await using var stream = await files[0].OpenReadAsync();
                using var streamReader = new StreamReader(stream);
                // Reads all the content of file as a text.
                fileContent = await streamReader.ReadToEndAsync();
                Editor.Document = new TextDocument(fileContent);
            }
        }


        private async void SaveFileButton_Clicked(object sender, RoutedEventArgs args)
        {


            // Get top level from the current control. Alternatively, you can use Window reference instead.
            var topLevel = TopLevel.GetTopLevel(this)!;

            // Start async operation to open the dialog.
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Text File",
                FileTypeChoices = new FilePickerFileType[] { new("GCode Files") { Patterns = new[] { "*.gcode", "*.txt", "*.nc" }, MimeTypes = new[] { "*/*" } } }
            })!;


            if (file is not null)
            {

                var stream = new MemoryStream(Encoding.Default.GetBytes(Editor.Document.Text));
                await using var writeStream = await file.OpenWriteAsync();
                await stream.CopyToAsync(writeStream);

            }

        }

        // Streams each non-empty line to your engine
        public async void StreamButton_Click(object sender, RoutedEventArgs e)
        {
            if (_client == null)
            {
                _responses.Clear();
                _responses.Add("✖ Client not initialized - cannot stream commands");
                return;
            }
            
            _responses.Clear();
            foreach (var line in Editor.Document.Lines
                        .Select(l => Editor.Document.GetText(l.Offset, l.Length))
                        .Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                // send each line and await the engine's reply
                var newline = "r " + line;
                var response = await _client.SendCommandAsync(newline);

                if (response is null)
                {
                    _responses.Add($"✖ {line} → No Response from engine");
                    // optionally: break;
                    continue;
                }
                if (!response.Status.Equals("OK", StringComparison.OrdinalIgnoreCase))
                {
                    _responses.Add($"✖ {line} → {response.Error ?? response.Result}");
                    break;
                }
                var RespSeg = response.Segments ?? Array.Empty<Segment>();
                // For each returned segment, display its joint angles & duration
                foreach (var seg in RespSeg)
                {
                    var angles = string.Join(", ", seg.JointAngles.Select(a => a.ToString("F2")));
                    _responses.Add($"✔ {line} ⇒ [{angles}] @ {seg.DurationMs}ms");
                }
            }

            //await TerminalPrint();
        }





    /*    private async Task TerminalPrint()
        {
            // 1) Turn each DocumentLine into its exact text
            var lines = Editor.Document.Lines
                        .Select(line =>
                        
                                Editor.Document.GetText(line.Offset, line.Length))
                        .ToArray();

            // 2) (Optional) log how many you got, so you can debug “is it empty?”
            Console.WriteLine($"[Debug] Found {lines.Length} lines in the document.");

            // 3) Now print (or send) each one
            foreach (var line in lines)
            {
                Console.WriteLine(line);
                // await SendToTerminalAsync(line);
                // await Task.Delay(...);  // if you need pacing
            }
            
        }
        */
        protected override void OnUnloaded(RoutedEventArgs e)
            {
                _client?.Dispose();
                base.OnUnloaded(e);
            }

    }
}
