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
using Avalonia.Platform.Storage;
using System.Reactive.Joins;             // for TextDocument

namespace KognaServer.ViewModels
{
    public partial class GCodeEditorViewModel : ObservableObject
    {

        private readonly TextEditor? _editor = null!;
        private string fileContent { get; set; }
        

        public GCodeEditorViewModel()
        {

        }

        

        


    }
}
