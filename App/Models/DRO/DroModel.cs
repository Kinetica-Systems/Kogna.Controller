using System;
using System.Collections.Generic;        // for EqualityComparer<>
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;   // for [CallerMemberName]
using System.Windows.Input;
using System.Threading.Tasks;

using Avalonia.Threading;

using KognaServer;
using KognaServer.Models;
using KognaServer.Views;
using KognaServer.Server.KognaServer;


namespace KognaServer.Models
{
    public class AxisInfo : INotifyPropertyChanged
    {
        public string Name { get; }
        private double _actual;
        public double Actual { get => _actual; set => SetField(ref _actual, value); }

        private double _target;
        public double Target { get => _target; set => SetField(ref _target, value); }

        private bool _enabled;
        public bool Enabled { get => _enabled; set => SetField(ref _enabled, value); }

        public AxisInfo(string name) => Name = name;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void SetField<T>(ref T field, T value, [CallerMemberName] string? propName = null)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
            }
        }
    }

    public class TcpPose : INotifyPropertyChanged
    {
        private double _x, _y, _z, _a, _b, _c;
        public double X { get => _x; set => SetField(ref _x, value); }
        public double Y { get => _y; set => SetField(ref _y, value); }
        public double Z { get => _z; set => SetField(ref _z, value); }
        public double A { get => _a; set => SetField(ref _a, value); }
        public double B { get => _b; set => SetField(ref _b, value); }
        public double C { get => _c; set => SetField(ref _c, value); }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void SetField<T>(ref T field, T value, [CallerMemberName] string? propName = null)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
            }
        }
    }
}
