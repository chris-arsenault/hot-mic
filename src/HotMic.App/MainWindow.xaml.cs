using System.Windows;

namespace HotMic.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        LocationChanged += (_, _) => UpdateWindowPosition();
        SizeChanged += (_, _) => UpdateWindowSize();
        Closed += (_, _) => DisposeViewModel();
    }

    private void UpdateWindowPosition()
    {
        if (DataContext is ViewModels.MainViewModel viewModel)
        {
            viewModel.UpdateWindowPosition(Left, Top);
        }
    }

    private void UpdateWindowSize()
    {
        if (DataContext is ViewModels.MainViewModel viewModel)
        {
            viewModel.UpdateWindowSize(Width, Height);
        }
    }

    private void DisposeViewModel()
    {
        if (DataContext is ViewModels.MainViewModel viewModel)
        {
            viewModel.Dispose();
        }
    }
}
