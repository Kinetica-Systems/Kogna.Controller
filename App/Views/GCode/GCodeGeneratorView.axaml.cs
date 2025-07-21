using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace KognaServer.Views;

public partial class GCodeGeneratorView : UserControl
{
    public GCodeGeneratorView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
} 