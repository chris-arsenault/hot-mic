using System;
using System.Windows.Input;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Analysis;
using SkiaSharp;

namespace HotMic.App.Views;

public partial class SpectrogramWindow : BaseVisualizerWindow
{
    private readonly SpectrogramRenderer _renderer;
    private IDisposable? _subscription;

    public SpectrogramWindow(AnalysisOrchestrator orchestrator)
        : base(orchestrator)
    {
        InitializeComponent();
        _renderer = new SpectrogramRenderer(Orchestrator.Results);

        // Set initial window size from renderer preference
        var preferredSize = SpectrogramRenderer.GetPreferredSize();
        Width = preferredSize.Width;
        Height = preferredSize.Height;
    }

    protected override IBaseVisualizerRenderer Renderer => _renderer;

    protected override void OnWindowLoaded(object? sender, System.Windows.RoutedEventArgs e)
    {
        // Subscribe to required analysis capabilities
        var caps = AnalysisCapabilities.Spectrogram |
                   AnalysisCapabilities.Pitch |
                   AnalysisCapabilities.Formants |
                   AnalysisCapabilities.Harmonics |
                   AnalysisCapabilities.VoicingState;

        _subscription = Orchestrator.Subscribe(caps);

        base.OnWindowLoaded(sender, e);
    }

    protected override void OnWindowClosed(object? sender, EventArgs e)
    {
        _subscription?.Dispose();
        _subscription = null;
        base.OnWindowClosed(sender, e);
    }

    protected override void OnRender(SKCanvas canvas, SKSize size)
    {
        _renderer.Render(canvas, size);
    }

    protected override void OnMouseDownOverride(float x, float y, MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            _renderer.HandleClick(x, y);
        }
    }
}
