using System;
using System.Collections.Generic;

namespace KinematicEngine.Core
{
    /// <summary>
    /// Represents a work coordinate system with offset from machine coordinates
    /// </summary>
    public class WorkCoordinateSystem
    {
        public int SystemNumber { get; set; }
        public double[] Offsets { get; set; } = new double[8];
        public bool IsActive { get; set; }
        public string Name { get; set; } = string.Empty;

        public WorkCoordinateSystem(int systemNumber)
        {
            SystemNumber = systemNumber;
            Name = systemNumber == 0 ? "Machine" : $"G{systemNumber + 53}";
        }

        /// <summary>
        /// Converts work coordinates to machine coordinates
        /// </summary>
        /// <param name="workPosition">Position in work coordinates</param>
        /// <returns>Position in machine coordinates</returns>
        public double[] ToMachineCoordinates(double[] workPosition)
        {
            var machinePosition = new double[8];
            for (int i = 0; i < 8; i++)
            {
                machinePosition[i] = workPosition[i] + Offsets[i];
            }
            return machinePosition;
        }

        /// <summary>
        /// Converts machine coordinates to work coordinates
        /// </summary>
        /// <param name="machinePosition">Position in machine coordinates</param>
        /// <returns>Position in work coordinates</returns>
        public double[] ToWorkCoordinates(double[] machinePosition)
        {
            var workPosition = new double[8];
            for (int i = 0; i < 8; i++)
            {
                workPosition[i] = machinePosition[i] - Offsets[i];
            }
            return workPosition;
        }

        /// <summary>
        /// Sets the offset for this coordinate system
        /// </summary>
        /// <param name="offsets">Offset values for each axis</param>
        public void SetOffsets(double[] offsets)
        {
            if (offsets.Length != 8)
                throw new ArgumentException("Offsets array must have 8 elements", nameof(offsets));
            
            Array.Copy(offsets, Offsets, 8);
        }

        /// <summary>
        /// Sets the offset for a specific axis
        /// </summary>
        /// <param name="axis">Axis index (0-7)</param>
        /// <param name="offset">Offset value</param>
        public void SetAxisOffset(int axis, double offset)
        {
            if (axis < 0 || axis >= 8)
                throw new ArgumentOutOfRangeException(nameof(axis), "Axis must be between 0 and 7");
            
            Offsets[axis] = offset;
        }

        /// <summary>
        /// Zeros the coordinate system at the current machine position
        /// </summary>
        /// <param name="currentMachinePosition">Current machine position</param>
        public void ZeroAtCurrentPosition(double[] currentMachinePosition)
        {
            if (currentMachinePosition.Length != 8)
                throw new ArgumentException("Position array must have 8 elements", nameof(currentMachinePosition));
            
            // Set offsets to negative of current machine position
            for (int i = 0; i < 8; i++)
            {
                Offsets[i] = -currentMachinePosition[i];
            }
        }
    }

    /// <summary>
    /// Manages multiple work coordinate systems
    /// </summary>
    public class CoordinateSystemManager
    {
        private readonly Dictionary<int, WorkCoordinateSystem> _coordinateSystems = new Dictionary<int, WorkCoordinateSystem>();
        private int _activeSystem = 0; // 0 = Machine coordinates (G53), 1 = G54, etc.

        public CoordinateSystemManager()
        {
            // Initialize with machine coordinates (G53)
            _coordinateSystems[0] = new WorkCoordinateSystem(0) { IsActive = true };
            
            // Initialize G54-G59 coordinate systems
            for (int i = 1; i <= 6; i++)
            {
                _coordinateSystems[i] = new WorkCoordinateSystem(i);
            }
        }

        /// <summary>
        /// Gets the currently active coordinate system
        /// </summary>
        public WorkCoordinateSystem ActiveSystem => _coordinateSystems[_activeSystem];

        /// <summary>
        /// Gets a specific coordinate system
        /// </summary>
        /// <param name="systemNumber">System number (0=Machine, 1=G54, etc.)</param>
        /// <returns>The coordinate system</returns>
        public WorkCoordinateSystem GetSystem(int systemNumber)
        {
            if (!_coordinateSystems.ContainsKey(systemNumber))
                throw new ArgumentException($"Coordinate system {systemNumber} does not exist", nameof(systemNumber));
            
            return _coordinateSystems[systemNumber];
        }

        /// <summary>
        /// Sets the active coordinate system
        /// </summary>
        /// <param name="systemNumber">System number (0=Machine, 1=G54, etc.)</param>
        public void SetActiveSystem(int systemNumber)
        {
            if (!_coordinateSystems.ContainsKey(systemNumber))
                throw new ArgumentException($"Coordinate system {systemNumber} does not exist", nameof(systemNumber));
            
            _coordinateSystems[_activeSystem].IsActive = false;
            _activeSystem = systemNumber;
            _coordinateSystems[_activeSystem].IsActive = true;
        }

        /// <summary>
        /// Converts work coordinates to machine coordinates using the active system
        /// </summary>
        /// <param name="workPosition">Position in work coordinates</param>
        /// <returns>Position in machine coordinates</returns>
        public double[] ToMachineCoordinates(double[] workPosition)
        {
            return ActiveSystem.ToMachineCoordinates(workPosition);
        }

        /// <summary>
        /// Converts machine coordinates to work coordinates using the active system
        /// </summary>
        /// <param name="machinePosition">Position in machine coordinates</param>
        /// <returns>Position in work coordinates</returns>
        public double[] ToWorkCoordinates(double[] machinePosition)
        {
            return ActiveSystem.ToWorkCoordinates(machinePosition);
        }

        /// <summary>
        /// Zeros the active coordinate system at the current machine position
        /// </summary>
        /// <param name="currentMachinePosition">Current machine position</param>
        public void ZeroActiveSystem(double[] currentMachinePosition)
        {
            ActiveSystem.ZeroAtCurrentPosition(currentMachinePosition);
        }

        /// <summary>
        /// Gets all coordinate systems
        /// </summary>
        /// <returns>Dictionary of all coordinate systems</returns>
        public IReadOnlyDictionary<int, WorkCoordinateSystem> GetAllSystems()
        {
            return _coordinateSystems;
        }

        /// <summary>
        /// Gets the active system number
        /// </summary>
        public int ActiveSystemNumber => _activeSystem;
    }
} 