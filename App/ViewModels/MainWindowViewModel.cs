using KognaServer;
using KognaServer.ViewModels;
using KognaComms;

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
        public KognaControl? _server { get; }
        public DroViewModel DroVm { get; }
        public TerminalViewModel TerminalViewModel { get; }
        public ConnectionViewModel ConnectionVm { get; }
        public GCodeEditorViewModel GcodeVm { get; }
        public GCodeGeneratorViewModel GCodeGeneratorViewModel { get; }
        public JoggingViewModel JoggingVm { get; }
        public Debug.DebugPanelViewModel DebugVm { get; }
        public StereoVisionViewModel StereoVisionVm { get; }
      
        // keep track of elapsed time (ms)


        public MainWindowViewModel(
            KognaControl? server,
            ConnectionViewModel connectionVm,
            DroViewModel droVm,
            TerminalViewModel terminalVm,
            GCodeEditorViewModel gCodeEditor,
            GCodeGeneratorViewModel gCodeGeneratorViewModel,
            JoggingViewModel joggingVm,
            Debug.DebugPanelViewModel debugVm,
            StereoVisionViewModel stereoVisionVm)
        {
            _server = server;
            ConnectionVm = connectionVm;
            DroVm = droVm;
            TerminalViewModel = terminalVm;
            GcodeVm = gCodeEditor;
            GCodeGeneratorViewModel = gCodeGeneratorViewModel;
            JoggingVm = joggingVm;
            DebugVm = debugVm;
            StereoVisionVm = stereoVisionVm;

        }
        
    }
}