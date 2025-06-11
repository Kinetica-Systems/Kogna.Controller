using KognaServer;
using KognaServer.ViewModels;
using KognaServer.Server.KognaServer;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Net;


namespace KognaServer.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private KognaServerMain _server { get; }
        public DroViewModel DroVm { get; }
        public TerminalViewModel TerminalViewModel { get; }
        public ConnectionViewModel ConnectionVm { get; }
        public GCodeEditorViewModel GcodeVm { get; }


        public MainWindowViewModel(
            KognaServerMain server,
            ConnectionViewModel connectionVm,
            DroViewModel droVm,
            TerminalViewModel terminalVm,
            GCodeEditorViewModel gCodeEditor)
        {
            _server = server;
            ConnectionVm = connectionVm;
            DroVm = droVm;
            TerminalViewModel = terminalVm;
            GcodeVm = gCodeEditor;

        }
    }
}