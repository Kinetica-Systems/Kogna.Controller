using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using System;

namespace KognaServer.Views
{
    public partial class AdvancedSettingsWindow : Window
    {
        public AdvancedSettingsWindow()
        {
            InitializeComponent();
        }


        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }



        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Dispose both VMs (they unsubscribe from events etc.)

        }
    }
}