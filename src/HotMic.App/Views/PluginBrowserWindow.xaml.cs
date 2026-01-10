using System.Windows;
using HotMic.App.ViewModels;

namespace HotMic.App.Views;

public partial class PluginBrowserWindow : Window
{
    public PluginBrowserWindow(PluginBrowserViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
    }

    private void OnCloseRequested(bool result)
    {
        DialogResult = result;
        Close();
    }
}
