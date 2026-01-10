using System.Windows;
using HotMic.App.ViewModels;

namespace HotMic.App.Views;

public partial class PluginParametersWindow : Window
{
    public PluginParametersWindow(PluginParametersViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += () => Close();
    }
}
