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

namespace KognaServer.ViewModels
{
    public partial class GCodeEditorViewModel : ObservableObject
    {
        private TextEditor? _editor;
        public TextEditor Editor
        {
            get => _editor ?? throw new InvalidOperationException("Editor not initialized");
            set => SetProperty(ref _editor, value);
        }

        [ObservableProperty]
        private string _currentFilePath = string.Empty;

        [ObservableProperty]
        private bool _isModified;

        public GCodeEditorViewModel()
        {
            // Initialize editor on UI thread to avoid thread-affinity exception
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                _editor = new TextEditor
                {
                    ShowLineNumbers = true,
                    WordWrap = true,
                    Document = new TextDocument()
                };
            });
        }

        public async Task LoadGCodeFile(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
                }

                var content = await File.ReadAllTextAsync(filePath);
                Editor.Document.Text = content;
                CurrentFilePath = filePath;
                IsModified = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading G-code file: {ex.Message}");
                throw; // Rethrow to let caller handle the error
            }
        }

        public void SetGCodeContent(string content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            Editor.Document.Text = content;
            IsModified = true;
        }

        public string GetGCodeContent()
        {
            return Editor.Document.Text;
        }

        public async Task SaveGCodeFile(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
                }

                await File.WriteAllTextAsync(filePath, Editor.Document.Text);
                CurrentFilePath = filePath;
                IsModified = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving G-code file: {ex.Message}");
                throw; // Rethrow to let caller handle the error
            }
        }
    }
}
