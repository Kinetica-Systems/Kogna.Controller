using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Input;
using KognaServer.Views;

namespace KognaServer.ViewModels
{
    // Simple ICommand implementation for native Avalonia MVVM
    public class DelegateCommand : ICommand
    {
        private readonly Action _execute;
        public DelegateCommand(Action execute) => _execute = execute;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
        public event EventHandler? CanExecuteChanged;
    }

    // Model representing a parameter entry
    public class ParameterItem : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string DataType { get; set; } = "string";
        private string _value = string.Empty;
        public string Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    OnPropertyChanged(nameof(Value));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // ViewModel for the Parameter Editor Window
    public class AdvancedSettingsWindowViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<string> Domains { get; } = new();
        public ObservableCollection<ParameterItem> Parameters { get; } = new();

        private string _selectedDomain = string.Empty;
        public string SelectedDomain
        {
            get => _selectedDomain;
            set
            {
                if (_selectedDomain != value)
                {
                    _selectedDomain = value;
                    OnPropertyChanged(nameof(SelectedDomain));
                    LoadParameters(value);
                }
            }
        }

        public ICommand SaveCommand { get; }
        private readonly string _connString = null!;
        private readonly string _robotName = "<YourRobotName>"; // TODO: set your actual robot name

        public AdvancedSettingsWindowViewModel()
        {
           // var dbPath = Path.Combine(AppContext.BaseDirectory, "parameters.db");
            //_connString = $"Data Source={dbPath};Version=3;Foreign Keys=True;";
            SaveCommand = new DelegateCommand(SaveParameters);
            //LoadDomains();
        }

        private void LoadDomains()
        {
            Domains.Clear();
            using var conn = new SQLiteConnection(_connString!);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM parameter_domains ORDER BY name;";
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
                Domains.Add(rdr.GetString(0));

            if (Domains.Any())
                SelectedDomain = Domains.First();
        }

        private void LoadParameters(string domain)
        {
            Parameters.Clear();
            using var conn = new SQLiteConnection(_connString!);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT p.id, p.key, p.data_type, COALESCE(v.value, p.default_value)
                FROM parameters p
                JOIN parameter_domains d ON p.domain_id = d.id
                LEFT JOIN parameter_values v
                  ON v.parameter_id = p.id AND v.robot_name = @robot
                WHERE d.name = @domain;";
            cmd.Parameters.AddWithValue("@robot", _robotName);
            cmd.Parameters.AddWithValue("@domain", domain);
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                Parameters.Add(new ParameterItem
                {
                    Id = rdr.GetInt32(0),
                    Key = rdr.GetString(1),
                    DataType = rdr.GetString(2),
                    Value = rdr.GetString(3)
                });
            }
        }

        private void SaveParameters()
        {
            /*  using var conn = new SQLiteConnection(_connString);
              conn.Open();
              using var tx = conn.BeginTransaction();

              foreach (var p in Parameters)
              {
                  using var cmd = conn.CreateCommand();
                  cmd.Transaction = tx;
                  cmd.CommandText = @"
                      INSERT INTO parameter_values(robot_name, parameter_id, value)
                      VALUES(@robot, @id, @val)
                      ON CONFLICT(robot_name, parameter_id) DO UPDATE SET
                        value = excluded.value,
                        last_updated = CURRENT_TIMESTAMP;";
                  cmd.Parameters.AddWithValue("@robot", _robotName);
                  cmd.Parameters.AddWithValue("@id", p.Id);
                  cmd.Parameters.AddWithValue("@val", p.Value);
                  cmd.ExecuteNonQuery();
              }

              tx.Commit();

  */
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
