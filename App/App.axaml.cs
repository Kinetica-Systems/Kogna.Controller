using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using KognaServer.ViewModels;
using KognaServer.Views;


using KognaServer.Server.KognaServer;
using System.Net;
using Avalonia.Metadata;


namespace KognaServer;


public partial class App : Application
{



    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {



            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

        var serverHost = new KognaServerMain("192.168.0.50", 2000);
        serverHost.Start();

        // 2) Create your two feature VMs, passing in the same server
        var droVm      = new DroViewModel(serverHost);
        var terminalVm = new TerminalViewModel(serverHost);
        var connectionVm = new ConnectionViewModel(serverHost);

        // 3) Now build the shell VM with both sub-VMs
            var mainVm = new MainWindowViewModel(serverHost, connectionVm, droVm, terminalVm);
            
            desktop.MainWindow = new MainWindow
                {
                    DataContext = mainVm
                };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}