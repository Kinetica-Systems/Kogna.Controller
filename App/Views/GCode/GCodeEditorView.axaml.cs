
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

namespace KognaServer.Views
{
    
    public partial class GCodeEditorView : UserControl
    {
        private readonly TextEditor? _editor = null!;
        public string fileContent { get; set; }
        public bool IsModified { get; set; }
        public TextDocument Document { get; set; }
        private GCodeStreamer bufferCommandFile{ get; set; } = null!;
        private String[] bufferedLines= [];



        public GCodeEditorView()
        {
            InitializeComponent();
            _editor = this.FindControl<TextEditor>("Editor");
            _editor.Background = Brushes.Transparent;
            _editor.Foreground = Brushes.LightGray;
            _editor.ShowLineNumbers = true;


        }

        private async void OpenFileButton_Clicked(object sender, RoutedEventArgs args)
        {
            // Get top level from the current control. Alternatively, you can use Window reference instead.
            var topLevel = TopLevel.GetTopLevel(this);

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
                _editor.Document = new TextDocument(fileContent);
           }
        }


        private async void SaveFileButton_Clicked(object sender, RoutedEventArgs args)
        {


               // Get top level from the current control. Alternatively, you can use Window reference instead.
            var topLevel = TopLevel.GetTopLevel(this);

                // Start async operation to open the dialog.
                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save Text File",
                    FileTypeChoices = new FilePickerFileType[] { new("GCode Files") { Patterns = new[] { "*.gcode", "*.txt", "*.nc" }, MimeTypes = new[] { "*/*" } } }
                });


                if (file is not null)
                {
                    
                    var stream = new MemoryStream(Encoding.Default.GetBytes(_editor.Document.Text));
                    await using var writeStream = await file.OpenWriteAsync();
                    await stream.CopyToAsync(writeStream);

                }

        }

        private async void OnStream_Click(object sender, RoutedEventArgs args)
        {
            // how many lines the editor thinks it has
            int lineCount = _editor.Document.LineCount;

            // allocate your array
            var bufferedLines = new string[lineCount];
            
           // Console.WriteLine("Checking", lineCount, bufferedLines);
            // for each DocumentLine, pull out the exact substring
            for (int i = 0; i < lineCount; i++)
            {
                var line = _editor.Document.Lines[i];
                // GetText(offset, length) returns just that one line’s text
                bufferedLines[i] = _editor.Document.GetText(line.Offset, line.Length);
            }
            TerminalPrint();



            // await stream.CopyToAsync(writeStream);

        }

                    private async Task TerminalPrint()
                    {
                        // 1) Turn each DocumentLine into its exact text
                        var lines = _editor.Document.Lines
                                    .Select(line => 
                                            _editor.Document.GetText(line.Offset, line.Length))
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
    }
}
