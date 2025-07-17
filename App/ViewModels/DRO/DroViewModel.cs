using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;


using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using KognaServer.Models;
using KognaServer.ViewModels;
using KognaServer.Views;
using KognaComms;
using TCPServer;

namespace KognaServer.ViewModels
{
    public partial class DroViewModel : ViewModelBase
    {
        public ObservableCollection<AxisInfo> Axes { get; }
        public TcpPose Pose { get; } = new TcpPose();
        private static readonly string[] sourceArray = ["X", "Y", "Z", "A", "B", "C"];
        public readonly KognaControl server = null!;

        // Current Cartesian Position
        [ObservableProperty]
        private double _currentX;
        [ObservableProperty]
        private double _currentY;
        [ObservableProperty]
        private double _currentZ;
        [ObservableProperty]
        private double _currentA;
        [ObservableProperty]
        private double _currentB;
        [ObservableProperty]
        private double _currentC;
        [ObservableProperty]
        private double _currentU;
        [ObservableProperty]
        private double _currentV;

        // Target Cartesian Position
        [ObservableProperty]
        private double _targetX;
        [ObservableProperty]
        private double _targetY;
        [ObservableProperty]
        private double _targetZ;
        [ObservableProperty]
        private double _targetA;
        [ObservableProperty]
        private double _targetB;
        [ObservableProperty]
        private double _targetC;
        [ObservableProperty]
        private double _targetU;
        [ObservableProperty]
        private double _targetV;

        // Joint Angles
        [ObservableProperty]
        private double _jointAngle1;
        [ObservableProperty]
        private double _jointAngle2;
        [ObservableProperty]
        private double _jointAngle3;
        [ObservableProperty]
        private double _jointAngle4;
        [ObservableProperty]
        private double _jointAngle5;
        [ObservableProperty]
        private double _jointAngle6;
        [ObservableProperty]
        private double _jointAngle7;
        [ObservableProperty]
        private double _jointAngle8;

        // Joint Names for display
        public string[] JointNames { get; } = ["J1", "J2", "J3", "J4", "J5", "J6", "J7", "J8"];
        
        public DroViewModel(KognaControl? server)
        {
            int axisCount = 6;
            if (server != null && server._coord != null)
            {
                try { server._coord.GetAxisDefinitions(); axisCount = server._coord.AxisCount; } catch { }
            }
            Axes = new ObservableCollection<AxisInfo>
            (
                Enumerable.Range(0, axisCount).Select(i => new AxisInfo($"{(char)('X'+i)}"))
            );

            // Subscribe to status updates from the server
            if (server != null && server._monitor != null)
            {
                server._monitor.OnStatusUpdate += OnStatusUpdate;
                Console.WriteLine("[DRO] Successfully subscribed to server status updates");
            }
            else
            {
                Console.WriteLine("[DRO] WARNING: Server or monitor is null, DRO will not receive updates");
            }
        }

        private void OnStatusUpdate(TCPServer.KognaMonitor.KognaStatus status)
        {
            // Update Cartesian positions
            CurrentX = status.CurrentX;
            CurrentY = status.CurrentY;
            CurrentZ = status.CurrentZ;
            CurrentA = status.CurrentA;
            CurrentB = status.CurrentB;
            CurrentC = status.CurrentC;
            CurrentU = status.CurrentU;
            CurrentV = status.CurrentV;

            // Update target positions
            TargetX = status.TargetX;
            TargetY = status.TargetY;
            TargetZ = status.TargetZ;
            TargetA = status.TargetA;
            TargetB = status.TargetB;
            TargetC = status.TargetC;
            TargetU = status.TargetU;
            TargetV = status.TargetV;

            // Update joint angles
            JointAngle1 = status.JointAngle1;
            JointAngle2 = status.JointAngle2;
            JointAngle3 = status.JointAngle3;
            JointAngle4 = status.JointAngle4;
            JointAngle5 = status.JointAngle5;
            JointAngle6 = status.JointAngle6;
            JointAngle7 = status.JointAngle7;
            JointAngle8 = status.JointAngle8;

            // Update axis info
            for (int i = 0; i < Axes.Count; i++)
            {
                if (i < status.JointsActual.Length)
                {
                    Axes[i].Actual = status.JointsActual[i];
                    Axes[i].Target = status.JointsTarget[i];
                    Axes[i].Enabled = status.JointsEnabled[i];
                }
            }
        }
    }
}
