using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.Linq;
using System.Threading.Tasks;
using KognaServer.ViewModels;
using KognaServer.Views;
using KognaServer.Server.KognaServer;

namespace KognaServer
{
    public partial class App : Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        // Use async void so we can await splash rendering and startup tasks
        public override async void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // 1) Show splash
                var splash = new SplashWindow();
                splash.Show();

                // 2) Prevent duplicate data-annotation validators
                splash.ReportProgress(10);
                DisableAvaloniaDataAnnotationValidation();

                // 3) Give the splash time to render
                await Task.Delay(100);

                // 4) Perform startup work off the UI thread
                var mainVm = await Task.Run(() =>
                {
                    // Start Kogna server
                    splash.ReportProgress(30);
                    var serverHost = new KognaServerMain("192.168.0.50", 2000);
                    serverHost.Start();

                    // Start IPC server
                    splash.ReportProgress(60);
                    var ipc = new SocketIpcServer(serverHost, port: 5000);
                    ipc.Start();

                    // Create sub-ViewModels
                    splash.ReportProgress(80);
                    var droVm           = new DroViewModel(serverHost);
                    var terminalVm      = new TerminalViewModel();
                    var connectionVm    = new ConnectionViewModel();
                    var GcodeVm         = new GCodeEditorViewModel();


                    // Build MainWindowViewModel
                    splash.ReportProgress(100);
                    return new MainWindowViewModel(serverHost, connectionVm, droVm, terminalVm, GcodeVm);
                });

                // 5) Initialize and show MainWindow
                var mainWindow = new MainWindow
                {
                    DataContext = mainVm
                };
                desktop.MainWindow = mainWindow;

                
                mainWindow.Show();

                // 6) Close the splash
                splash.Close();
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void DisableAvaloniaDataAnnotationValidation()
        {
            var pluginsToRemove = BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();
            foreach (var plugin in pluginsToRemove)
                BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}