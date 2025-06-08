using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Threading;

using System;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using KognaServer.ViewModels;
using KognaServer.Server;
using KognaServer.Server.KognaServer;
using KognaServer.Views;

namespace KognaServer.Views;

public partial class MainWindow : Window
{





    public MainWindow()
    {
        InitializeComponent();





    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // Dispose both VMs (they unsubscribe from events etc.)

    }
    
}