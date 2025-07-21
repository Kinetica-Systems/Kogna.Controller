using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KognaComms;
using KognaServer.Models;

namespace KognaServer.ViewModels;

public partial class GCodeGeneratorViewModel : ViewModelBase
{
    private readonly AppIpcClient _client;

    [ObservableProperty]
    private string _selectedStlFile = string.Empty;

    [ObservableProperty]
    private double _layerHeight = 0.2;

    [ObservableProperty]
    private int _perimeterCount = 3;

    [ObservableProperty]
    private double _infillDensity = 0.2;

    [ObservableProperty]
    private string _selectedInfillPattern = "grid";

    [ObservableProperty]
    private double _extrusionWidth = 0.4;

    [ObservableProperty]
    private double _printSpeed = 60;

    [ObservableProperty]
    private double _travelSpeed = 120;

    [ObservableProperty]
    private double _retractLength = 4;

    [ObservableProperty]
    private double _retractSpeed = 45;

    [ObservableProperty]
    private double _hotendTemp = 200;

    [ObservableProperty]
    private double _bedTemp = 60;

    [ObservableProperty]
    private bool _saveToFile = true;

    [ObservableProperty]
    private string _outputPath = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    public ObservableCollection<string> InfillPatterns { get; } = new()
    {
        "grid",
        "lines",
        "triangles"
    };

    public GCodeGeneratorViewModel(AppIpcClient client)
    {
        _client = client;
        SelectStlFileCommand = new AsyncRelayCommand(SelectStlFile);
        SelectOutputPathCommand = new AsyncRelayCommand(SelectOutputPath);
        GenerateGCodeCommand = new AsyncRelayCommand(GenerateGCode);
    }

    public ICommand SelectStlFileCommand { get; }
    public ICommand SelectOutputPathCommand { get; }
    public ICommand GenerateGCodeCommand { get; }

    private async Task SelectStlFile()
    {
        #pragma warning disable CS0618 // OpenFileDialog obsolete - to be migrated in future release
        var dialog = new OpenFileDialog
        {
            Title = "Select STL File",
            Filters = new List<FileDialogFilter>
            {
                new FileDialogFilter { Name = "STL Files", Extensions = new List<string> { "stl" } }
            }
        };

        var result = await dialog.ShowAsync(new Window());
        if (result != null && result.Length > 0)
        {
            SelectedStlFile = result[0];
            if (string.IsNullOrEmpty(OutputPath))
            {
                OutputPath = Path.ChangeExtension(SelectedStlFile, ".gcode");
            }
        }
        #pragma warning restore CS0618 // OpenFileDialog obsolete - to be migrated in future release
    }

    private async Task SelectOutputPath()
    {
        #pragma warning disable CS0618 // SaveFileDialog obsolete - to be migrated
        var dialog = new SaveFileDialog
        {
            Title = "Save G-code File",
            InitialFileName = Path.GetFileNameWithoutExtension(SelectedStlFile) + ".gcode",
            DefaultExtension = ".gcode",
            Filters = new List<FileDialogFilter>
            {
                new FileDialogFilter { Name = "G-code Files", Extensions = new List<string> { "gcode" } }
            }
        };

        var result = await dialog.ShowAsync(new Window());
        if (!string.IsNullOrEmpty(result))
        {
            OutputPath = result;
        }
        #pragma warning restore CS0618 // SaveFileDialog obsolete - to be migrated
    }

    private async Task GenerateGCode()
    {
        if (string.IsNullOrEmpty(SelectedStlFile))
        {
            Status = "Please select an STL file first";
            return;
        }

        if (SaveToFile && string.IsNullOrEmpty(OutputPath))
        {
            Status = "Please select an output path for the G-code file";
            return;
        }

        try
        {
            Status = "Loading STL file...";
            var loadResponse = await _client.SendCommandAsync($"loadstl {SelectedStlFile}");
            if (!loadResponse.Status.Equals("OK", StringComparison.OrdinalIgnoreCase))
            {
                Status = $"Failed to load STL: {loadResponse.Error}";
                return;
            }

            Status = "Slicing model...";
            var sliceCmd = $"slice {LayerHeight} {PerimeterCount} {InfillDensity} {SelectedInfillPattern}";
            var sliceResponse = await _client.SendCommandAsync(sliceCmd);
            if (!sliceResponse.Status.Equals("OK", StringComparison.OrdinalIgnoreCase))
            {
                Status = $"Failed to slice model: {sliceResponse.Error}";
                return;
            }

            Status = "Generating G-code...";
            var previewResponse = await _client.SendCommandAsync("preview");
            if (!previewResponse.Status.Equals("OK", StringComparison.OrdinalIgnoreCase))
            {
                Status = $"Failed to generate G-code: {previewResponse.Error}";
                return;
            }

            var gcode = previewResponse.Result;

            if (SaveToFile)
            {
                await File.WriteAllTextAsync(OutputPath, gcode);
                Status = $"G-code saved to {OutputPath}";

                // Load the generated G-code into the editor
                var loadCmd = $"loadgcode {OutputPath}";
                var loadGCodeResponse = await _client.SendCommandAsync(loadCmd);
                if (!loadGCodeResponse.Status.Equals("OK", StringComparison.OrdinalIgnoreCase))
                {
                    Status = $"Warning: Failed to load G-code into editor: {loadGCodeResponse.Error}";
                }
            }
            else
            {
                // Load the G-code directly into the editor
                var loadCmd = $"setgcode {gcode}";
                var loadGCodeResponse = await _client.SendCommandAsync(loadCmd);
                if (!loadGCodeResponse.Status.Equals("OK", StringComparison.OrdinalIgnoreCase))
                {
                    Status = $"Warning: Failed to load G-code into editor: {loadGCodeResponse.Error}";
                }
            }
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
    }
} 