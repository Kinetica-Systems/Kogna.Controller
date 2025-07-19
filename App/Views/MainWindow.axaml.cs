using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Diagnostics;

using System;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using KognaServer.ViewModels;
using KognaServer.Server;
using KognaComms;
using KognaServer.Views;

namespace KognaServer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
#if DEBUG
        // AttachDevTools removed to fix build error - requires Avalonia.Diagnostics package
        // which has version conflicts with current Avalonia version
#endif
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }


    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // Dispose both VMs (they unsubscribe from events etc.)

    }
    
}