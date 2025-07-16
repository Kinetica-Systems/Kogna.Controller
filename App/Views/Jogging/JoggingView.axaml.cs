using Avalonia.Controls;
using Avalonia.Input;
using KognaServer.ViewModels;

namespace KognaServer.Views
{
    public partial class JoggingView : UserControl
    {
        public JoggingView()
        {
            InitializeComponent();
            
            // Set up keyboard event handling
            this.KeyDown += JoggingView_KeyDown;
        }

        private void JoggingView_KeyDown(object? sender, KeyEventArgs e)
        {
            if (DataContext is JoggingViewModel viewModel)
            {
                viewModel.HandleKeyPress(e.Key);
                e.Handled = true; // Prevent event bubbling
            }
        }
    }
} 