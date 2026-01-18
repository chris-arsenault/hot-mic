using System;
using System.Windows;
using System.Windows.Threading;
using HotMic.Vst3;

namespace HotMic.App.Views;

public partial class Vst3EditorWindow : Window, IDisposable
{
    private readonly Vst3PluginWrapper _plugin;
    private readonly DispatcherTimer _idleTimer;
    private readonly System.Windows.Forms.Panel _panel;
    private bool _disposed;

    public Vst3EditorWindow(Vst3PluginWrapper plugin)
    {
        InitializeComponent();
        _plugin = plugin;
        _panel = new System.Windows.Forms.Panel { Dock = System.Windows.Forms.DockStyle.Fill };
        EditorHost.Child = _panel;

        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _idleTimer.Tick += (_, _) => _plugin.EditorIdle();

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_plugin.TryGetEditorRect(out var rect))
        {
            Width = rect.Width + 16;
            Height = rect.Height + 40;
        }

        if (!_plugin.OpenEditor(_panel.Handle))
        {
            SkiaMessageDialog.ShowError(this, "Failed to open VST3 editor.", "HotMic");
            Close();
            return;
        }

        _idleTimer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _idleTimer.Stop();
        _plugin.CloseEditor();
        _panel.Dispose();
        GC.SuppressFinalize(this);
    }
}
