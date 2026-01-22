using System.Windows;
using HotMic.App.Views;
using HotMic.Core.Analysis;

namespace HotMic.App.ViewModels;

internal sealed class MainWindowRouter
{
    public bool ShowSettings(SettingsViewModel viewModel)
    {
        var window = new SettingsWindow(viewModel)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        return window.ShowDialog() == true;
    }

    public void ShowAnalyzerWindow(AnalysisOrchestrator orchestrator)
    {
        var window = new AnalyzerWindow(orchestrator)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    public void ShowAnalysisSettingsWindow(AnalysisOrchestrator orchestrator)
    {
        var window = new AnalysisSettingsWindow(orchestrator)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    public void ShowWaveformWindow(AnalysisOrchestrator orchestrator)
    {
        var window = new WaveformWindow(orchestrator)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    public void ShowSpeechCoachWindow(AnalysisOrchestrator orchestrator)
    {
        var window = new SpeechCoachWindow(orchestrator)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }
}
