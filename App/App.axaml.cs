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
using KognaServer.Server;

using KognaComms;
using KognaServer.Models;

namespace KognaServer
{
    public partial class App : Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);


        // Use async void so we can await splash rendering and startup tasks
        public override async void OnFrameworkInitializationCompleted()
        {
            Console.WriteLine("[APP] Framework initialization completed");
            
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                Console.WriteLine("[APP] Creating splash window");
                // 1) Show splash
                var splash = new SplashWindow();
                splash.Show();

                // 2) Prevent duplicate data-annotation validators
                splash.ReportProgress(10);
                DisableAvaloniaDataAnnotationValidation();

                // 3) Give the splash time to render
                //await Task.Delay(100);

                // 4) Perform startup work off the UI thread
                Console.WriteLine("[APP] Starting background initialization");
                KognaControl? serverHost = null;
                try
                {
                    Console.WriteLine("[APP] Creating KognaControl for 192.168.0.50:2000");
                    serverHost = new KognaControl("192.168.0.50", 2000);

                    Console.WriteLine("[APP] Starting server...");
                    bool startResult = await serverHost.Start();
                    Console.WriteLine($"[APP] Server start result: {startResult}");

                    if (!startResult)
                    {
                        Console.WriteLine("[APP] WARNING: Server failed to start, but continuing with UI");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[APP] ERROR during server startup: {ex.Message}");
                    Console.WriteLine($"[APP] Stack trace: {ex.StackTrace}");
                    // Continue with null serverHost - UI will show disconnected state
                }

                var mainVm = await Task.Run(() =>
                {
                    // Create sub-ViewModels
                    Console.WriteLine("[APP] Creating ViewModels");
                    splash.ReportProgress(60);
                    var droVm = new DroViewModel(serverHost);
                    var terminalVm = new TerminalViewModel();
                    var connectionVm = new ConnectionViewModel();
                    var GcodeVm = new GCodeEditorViewModel();
                    var gCodeGeneratorVm = new GCodeGeneratorViewModel(new AppIpcClient(serverHost));
                    var joggingVm = new JoggingViewModel(serverHost);
                    var debugVm  = new KognaServer.ViewModels.Debug.DebugPanelViewModel();
                    // var Advanced        = new AdvancedSettingsWindowViewModel();

                    // Build MainWindowViewModel
                    Console.WriteLine("[APP] Creating MainWindowViewModel");
                    splash.ReportProgress(100);
                    return new MainWindowViewModel(serverHost, connectionVm, droVm, terminalVm, GcodeVm, gCodeGeneratorVm, joggingVm, debugVm);
                });

                // 5) Initialize and show MainWindow
                Console.WriteLine("[APP] Creating MainWindow");
                var mainWindow = new MainWindow
                {
                    DataContext = mainVm
                };
                desktop.MainWindow = mainWindow;

                Console.WriteLine("[APP] Showing MainWindow");
                mainWindow.Show();

                // 6) Close the splash
                splash.Close();
                Console.WriteLine("[APP] Application startup complete");
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