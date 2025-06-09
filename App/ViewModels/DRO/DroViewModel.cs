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
using KognaServer.Server.KognaServer;




namespace KognaServer.ViewModels
{
    public class DroViewModel : ViewModelBase
    {
        public ObservableCollection<AxisInfo> Axes { get; }
        public TcpPose Pose { get; } = new TcpPose();
        private static readonly string[] sourceArray = ["X", "Y", "Z", "A", "B", "C"];


        
        public DroViewModel(KognaServerMain server)
        {
            Axes = new ObservableCollection<AxisInfo>
            (
            sourceArray.Select(n => new AxisInfo(n))
            );

            server.OnStatusUpdate += s =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    for (int i = 0; i < 6; i++)
                    {
                        Axes[i].Actual = s.JointsActual[i];
                        Axes[i].Target = s.JointsTarget[i];
                        Axes[i].Enabled = s.JointsEnabled[i];
                    }

                });
            };
        }

       


        
    }
}
