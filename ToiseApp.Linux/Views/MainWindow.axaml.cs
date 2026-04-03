using Avalonia.Controls;
using ToiseApp.Linux.ViewModels;

namespace ToiseApp.Linux.Views;

public partial class MainWindow : Window
{
    public MainWindow(ToiseViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    protected override void OnClosed(System.EventArgs e)
    {
        base.OnClosed(e);
        (DataContext as ToiseViewModel)?.Dispose();
    }
}