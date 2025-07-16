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
using TCPServer; // Add for KognaStatus




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
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        // Update current cartesian positions
                        if (status.JointsActual.Length > 0) CurrentX = status.CurrentX;
                        if (status.JointsActual.Length > 1) CurrentY = status.CurrentY;
                        if (status.JointsActual.Length > 2) CurrentZ = status.CurrentZ;
                        if (status.JointsActual.Length > 3) CurrentA = status.CurrentA;
                        if (status.JointsActual.Length > 4) CurrentB = status.CurrentB;
                        if (status.JointsActual.Length > 5) CurrentC = status.CurrentC;
                        if (status.JointsActual.Length > 6) CurrentU = status.CurrentU;
                        if (status.JointsActual.Length > 7) CurrentV = status.CurrentV;

                        // Update target cartesian positions
                        if (status.JointsTarget.Length > 0) TargetX = status.TargetX;
                        if (status.JointsTarget.Length > 1) TargetY = status.TargetY;
                        if (status.JointsTarget.Length > 2) TargetZ = status.TargetZ;
                        if (status.JointsTarget.Length > 3) TargetA = status.TargetA;
                        if (status.JointsTarget.Length > 4) TargetB = status.TargetB;
                        if (status.JointsTarget.Length > 5) TargetC = status.TargetC;
                        if (status.JointsTarget.Length > 6) TargetU = status.TargetU;
                        if (status.JointsTarget.Length > 7) TargetV = status.TargetV;

                        // Update joint angles
                        if (status.JointsActual.Length > 0) JointAngle1 = status.JointAngle1;
                        if (status.JointsActual.Length > 1) JointAngle2 = status.JointAngle2;
                        if (status.JointsActual.Length > 2) JointAngle3 = status.JointAngle3;
                        if (status.JointsActual.Length > 3) JointAngle4 = status.JointAngle4;
                        if (status.JointsActual.Length > 4) JointAngle5 = status.JointAngle5;
                        if (status.JointsActual.Length > 5) JointAngle6 = status.JointAngle6;
                        if (status.JointsActual.Length > 6) JointAngle7 = status.JointAngle7;
                        if (status.JointsActual.Length > 7) JointAngle8 = status.JointAngle8;

                        // Update the axes collection for the DataGrid
                        for (int i = 0; i < Math.Min(status.JointsActual.Length, Axes.Count); i++)
                        {
                            Axes[i].Actual = status.JointsActual[i];
                            Axes[i].Target = status.JointsTarget[i];
                            Axes[i].Enabled = status.JointsEnabled[i];
                        }

                        Console.WriteLine($"[DRO] Updated positions - Current: ({CurrentX:F3}, {CurrentY:F3}, {CurrentZ:F3}) Target: ({TargetX:F3}, {TargetY:F3}, {TargetZ:F3})");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DRO] ERROR in status update UI thread: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DRO] ERROR in status update: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (server != null && server._monitor != null)
            {
                server._monitor.OnStatusUpdate -= OnStatusUpdate;
            }
        }
    }
}
