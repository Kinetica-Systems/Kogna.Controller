// GCodeEditorViewModel.cs
using System.IO;
using Avalonia.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;                        // for FileDialogFilter
using Avalonia.Controls.ApplicationLifetimes;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AvaloniaEdit.Document;                    // for TextDocument

namespace KognaServer.ViewModels
{
    public partial class GCodeEditorViewModel : ObservableObject
    {
        public TextDocument Document { get; set; } = new TextDocument();
        public IAsyncRelayCommand OpenGCodeCommand { get; }
        public IAsyncRelayCommand SaveGCodeCommand { get; }

        public GCodeEditorViewModel()
        {
            Document = new TextDocument();
            OpenGCodeCommand = new AsyncRelayCommand(OpenFileAsync);
            SaveGCodeCommand = new AsyncRelayCommand(SaveFileAsync);
        }

        private async Task OpenFileAsync()
        {
            var dlg = new OpenFileDialog {
                Title = "Open G-code File",
                Filters = { new FileDialogFilter { Name="G-code", Extensions={ "gcode","nc","txt"} } }
            };
            var main = (Avalonia.Application.Current.ApplicationLifetime
                        as IClassicDesktopStyleApplicationLifetime)!.MainWindow;
            var files = await dlg.ShowAsync(main);
            if (files?.Length > 0)
            {
                // read off-thread
                var text = await File.ReadAllTextAsync(files[0]);

                // now update on UI thread
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // this will now succeed, because we created Document on UI thread
                    Document.Text = text;
                }, Avalonia.Threading.DispatcherPriority.Send);
            }
        }

        private async Task SaveFileAsync()
        {
            var dlg = new SaveFileDialog {
                Title = "Save G-code File",
                DefaultExtension="gcode",
                Filters = { new FileDialogFilter { Name="G-code", Extensions={ "gcode","nc","txt"} } }
            };
            var main = (Avalonia.Application.Current.ApplicationLifetime
                        as IClassicDesktopStyleApplicationLifetime)!.MainWindow;
            var path = await dlg.ShowAsync(main);
            if (!string.IsNullOrEmpty(path))
            {
                // grab the text back on UI thread, then write it
                var content = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                    () => Document.Text,
                    Avalonia.Threading.DispatcherPriority.Send);
                await File.WriteAllTextAsync(path, content);
            }
        }
    }
}
