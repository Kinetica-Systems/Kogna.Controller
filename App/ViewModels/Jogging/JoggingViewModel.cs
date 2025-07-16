using System;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KognaComms;
using Avalonia.Input;

namespace KognaServer.ViewModels
{
    public partial class JoggingViewModel : ViewModelBase
    {
        private readonly KognaControl? _server;

        [ObservableProperty]
        private string _activeCoordinateSystem = "G54";

        [ObservableProperty]
        private double _jogStep = 1.0;

        [ObservableProperty]
        private double _jogSpeed = 50.0;

        [ObservableProperty]
        private bool _isJogging = false;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        public JoggingViewModel(KognaControl? server)
        {
            _server = server;
            // Initialize with default values
            ActiveCoordinateSystem = "G54";
            JogStep = 1.0;
            JogSpeed = 50.0;
            IsJogging = false;
            StatusMessage = "Ready";
        }

        [RelayCommand]
        private async Task JogXPositive()
        {
            await JogAxis("X", JogStep);
        }

        [RelayCommand]
        private async Task JogXNegative()
        {
            await JogAxis("X", -JogStep);
        }

        [RelayCommand]
        private async Task JogYPositive()
        {
            await JogAxis("Y", JogStep);
        }

        [RelayCommand]
        private async Task JogYNegative()
        {
            await JogAxis("Y", -JogStep);
        }

        [RelayCommand]
        private async Task JogZPositive()
        {
            await JogAxis("Z", JogStep);
        }

        [RelayCommand]
        private async Task JogZNegative()
        {
            await JogAxis("Z", -JogStep);
        }

        [RelayCommand]
        private async Task JogAPositive()
        {
            await JogAxis("A", JogStep);
        }

        [RelayCommand]
        private async Task JogANegative()
        {
            await JogAxis("A", -JogStep);
        }

        [RelayCommand]
        private async Task JogBPositive()
        {
            await JogAxis("B", JogStep);
        }

        [RelayCommand]
        private async Task JogBNegative()
        {
            await JogAxis("B", -JogStep);
        }

        [RelayCommand]
        private async Task JogCPositive()
        {
            await JogAxis("C", JogStep);
        }

        [RelayCommand]
        private async Task JogCNegative()
        {
            await JogAxis("C", -JogStep);
        }

        [RelayCommand]
        private async Task SetCoordinateSystem(string system)
        {
            if (_server == null || _server._engine == null) 
            {
                StatusMessage = "Server not connected";
                return;
            }

            try
            {
                IsJogging = true;
                StatusMessage = $"Setting coordinate system to {system}...";

                int systemNumber = system switch
                {
                    "G53" => 0,
                    "G54" => 1,
                    "G55" => 2,
                    "G56" => 3,
                    "G57" => 4,
                    "G58" => 5,
                    "G59" => 6,
                    _ => 1 // Default to G54
                };

                var result = await _server.ProcessIpcCommand($"setcs {systemNumber}");
                if (result.response.Contains("Error"))
                {
                    StatusMessage = $"Error: {result.response}";
                }
                else
                {
                    ActiveCoordinateSystem = system;
                    StatusMessage = $"Coordinate system set to {system}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error setting coordinate system: {ex.Message}";
            }
            finally
            {
                IsJogging = false;
            }
        }

        [RelayCommand]
        private async Task ZeroCurrentPosition()
        {
            if (_server == null || _server._engine == null) 
            {
                StatusMessage = "Server not connected";
                return;
            }

            try
            {
                IsJogging = true;
                StatusMessage = "Zeroing coordinate system at current position...";

                var result = await _server.ProcessIpcCommand("zero");
                if (result.response.Contains("Error"))
                {
                    StatusMessage = $"Error: {result.response}";
                }
                else
                {
                    StatusMessage = $"Zeroed {ActiveCoordinateSystem} at current position";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error zeroing coordinate system: {ex.Message}";
            }
            finally
            {
                IsJogging = false;
            }
        }

        [RelayCommand]
        private async Task GetCoordinateSystem()
        {
            if (_server == null || _server._engine == null) 
            {
                StatusMessage = "Server not connected";
                return;
            }

            try
            {
                var result = await _server.ProcessIpcCommand("getcs");
                if (result.response.Contains("Error"))
                {
                    StatusMessage = $"Error: {result.response}";
                }
                else
                {
                    StatusMessage = result.response;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error getting coordinate system: {ex.Message}";
            }
        }

        private async Task JogAxis(string axis, double distance)
        {
            if (_server == null || _server._engine == null) 
            {
                StatusMessage = "Server not connected";
                return;
            }

            try
            {
                IsJogging = true;
                StatusMessage = $"Jogging {axis} by {distance}...";

                var result = await _server.ProcessIpcCommand($"jog {axis} {distance}");
                if (result.response.Contains("Error"))
                {
                    StatusMessage = $"Error: {result.response}";
                }
                else
                {
                    StatusMessage = result.response;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error during jog: {ex.Message}";
            }
            finally
            {
                IsJogging = false;
            }
        }

        // Keyboard shortcuts for jogging
        public void HandleKeyPress(Key key)
        {
            if (IsJogging) return; // Prevent multiple jog commands

            switch (key)
            {
                case Key.Up:
                    _ = JogYPositiveCommand.ExecuteAsync(null);
                    break;
                case Key.Down:
                    _ = JogYNegativeCommand.ExecuteAsync(null);
                    break;
                case Key.Left:
                    _ = JogXNegativeCommand.ExecuteAsync(null);
                    break;
                case Key.Right:
                    _ = JogXPositiveCommand.ExecuteAsync(null);
                    break;
                case Key.PageUp:
                    _ = JogZPositiveCommand.ExecuteAsync(null);
                    break;
                case Key.PageDown:
                    _ = JogZNegativeCommand.ExecuteAsync(null);
                    break;
                case Key.Home:
                    _ = JogAPositiveCommand.ExecuteAsync(null);
                    break;
                case Key.End:
                    _ = JogANegativeCommand.ExecuteAsync(null);
                    break;
                case Key.Insert:
                    _ = JogBPositiveCommand.ExecuteAsync(null);
                    break;
                case Key.Delete:
                    _ = JogBNegativeCommand.ExecuteAsync(null);
                    break;
            }
        }
    }
} 