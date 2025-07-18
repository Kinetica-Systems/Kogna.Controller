using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ScottPlot;
using ScottPlot.Avalonia;
using System;
using System.Linq;
using KognaServer.ViewModels.Debug;
using Avalonia.Threading;

namespace KognaServer.Views;
public partial class DebugPanel : UserControl
{
    private AvaPlot? _beadPlot;
    private AvaPlot? _powerPlot;
    private ScottPlot.Plottables.DataStreamer? _beadStream;
    private ScottPlot.Plottables.DataStreamer? _powerStream;

    private DebugPanelViewModel? _viewModel;

    public DebugPanel()
    {
        InitializeComponent();

        // set up static plot instance references
        _beadPlot = this.FindControl<AvaPlot>("BeadPlot");
        _powerPlot = this.FindControl<AvaPlot>("PowerPlot");

        if (_beadPlot != null)
        {
            _beadStream = _beadPlot.Plot.Add.DataStreamer(2000);
            _beadPlot.Plot.Title("Bead Width (mm)");
        }

        if (_powerPlot != null)
        {
            _powerStream = _powerPlot.Plot.Add.DataStreamer(2000);
            _powerPlot.Plot.Title("Laser Power (W)");
        }

        // respond when the view model is attached or changed
        this.DataContextChanged += OnDataContextChanged;

        // attempt to wire-up immediately if DataContext already present (designer / runtime)
        AttachToViewModel(DataContext as DebugPanelViewModel);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        AttachToViewModel(DataContext as DebugPanelViewModel);
    }

    private void AttachToViewModel(DebugPanelViewModel? vm)
    {
        if (_viewModel == vm)
            return;

        // detach from old VM
        if (_viewModel != null)
        {
            _viewModel.BeadSample -= BeadSampleHandler;
            _viewModel.PowerSample -= PowerSampleHandler;
        }

        _viewModel = vm;

        // attach to new VM
        if (_viewModel != null)
        {
            _viewModel.BeadSample += BeadSampleHandler;
            _viewModel.PowerSample += PowerSampleHandler;
        }
    }

    private void BeadSampleHandler(double value)
    {
        Dispatcher.UIThread.Post(() => PushBead(value));
    }

    private void PowerSampleHandler(double value)
    {
        Dispatcher.UIThread.Post(() => PushPower(value));
    }

    private void PushBead(double value)
    {
        if (_beadStream == null || _beadPlot == null) return;
        _beadStream.Add(value);
        _beadPlot.Refresh();
    }

    private void PushPower(double value)
    {
        if (_powerStream == null || _powerPlot == null) return;
        _powerStream.Add(value);
        _powerPlot.Refresh();
    }
} 