using System;
using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
// Plotting removed for now
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharedTypes;

namespace KognaServer.ViewModels.Debug;

public partial class DebugPanelViewModel : ObservableObject
{
    // Plot data placeholders
    public ObservableCollection<BeadMeasurement> Measurements { get; } = new();
    public ObservableCollection<LogEntry> Events { get; } = new();

    // Events raised when new samples are available
    public event Action<double>? BeadSample;
    public event Action<double>? PowerSample;

    [ObservableProperty]
    private Bitmap? _cameraFrame;

    public DebugPanelViewModel()
    {
        // No plotting library currently compatible with Avalonia 11; placeholder only
    }

    public void OnMeasurement(BeadMeasurement m)
    {
        Measurements.Add(m);
        if (Measurements.Count > 1000) Measurements.RemoveAt(0);

        BeadSample?.Invoke(m.MeasuredWidth);
        // could push measurement to debug log or UI later
    }

    public void OnMotionCommand(MotionCommand cmd)
    {
        // Placeholder for real-time parameter logging
        PowerSample?.Invoke(cmd.LaserPower);
    }

    [RelayCommand]
    private void Clear()
    {
        // plots removed
        Events.Clear();
    }

    public record LogEntry(DateTime Timestamp, string Category, string Message);
} 