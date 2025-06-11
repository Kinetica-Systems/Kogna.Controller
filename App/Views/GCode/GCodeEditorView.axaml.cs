using System.IO;
using Avalonia;
using AvaloniaEdit;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AvaloniaInteractivity = Avalonia.Interactivity;

namespace KognaServer.Views
{
    public partial class GCodeEditorView : UserControl
    {
        private TextEditor _editor;
        public GCodeEditorView()
        {
            InitializeComponent();
            _editor = this.FindControl<TextEditor>("Editor");  // make sure XAML name matches
        }

        private void InitializeComponent()
            => AvaloniaXamlLoader.Load(this);

        private async void OnOpenClicked(object? sender, AvaloniaInteractivity.RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title   = "Open G-code File",
                Filters = { new FileDialogFilter { Name = "G-code", Extensions = { "gcode", "nc", "txt" } } }
            };

            // get the main window so the dialog shows modally
            var lifetime = Application.Current.ApplicationLifetime
                               as IClassicDesktopStyleApplicationLifetime;
            var window   = lifetime?.MainWindow;
            if (window == null) return;

            var result = await dlg.ShowAsync(window);
            if (result?.Length > 0)
            {
                // this code is back on the UI thread automatically
                var text = await File.ReadAllTextAsync(result[0]);
                _editor.Text = text;
            }
        }

        private async void OnSaveClicked(object? sender, AvaloniaInteractivity.RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title            = "Save G-code File",
                DefaultExtension = "gcode",
                Filters          = { new FileDialogFilter { Name = "G-code", Extensions = { "gcode", "nc", "txt" } } }
            };

            var lifetime = Application.Current.ApplicationLifetime
                               as IClassicDesktopStyleApplicationLifetime;
            var window   = lifetime?.MainWindow;
            if (window == null) return;

            var path = await dlg.ShowAsync(window);
            if (!string.IsNullOrEmpty(path))
            {
                await File.WriteAllTextAsync(path, _editor.Text);
            }
        }
    }
}
