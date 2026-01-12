using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using HotMic.App.UI;
using HotMic.App.ViewModels;
using HotMic.Common.Configuration;
using SkiaSharp;
using SkiaSharp.Views.WPF;

namespace HotMic.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsRenderer _renderer = new();
    private readonly SettingsUiState _uiState = new();
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        viewModel.CloseRequested += OnCloseRequested;
    }

    private void OnCloseRequested(bool result)
    {
        DialogResult = result;
        Close();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();
        _renderer.Render(canvas, size, _viewModel, _uiState, dpiScale);
    }

    private void SkiaCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        if (_renderer.HitTestTitleBar(x, y))
        {
            DragMove();
            e.Handled = true;
            return;
        }

        if (_renderer.HitTestApply(x, y))
        {
            _viewModel.ApplyCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (_renderer.HitTestCancel(x, y))
        {
            _viewModel.CancelCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (_renderer.HitTestVstCheckbox(x, y))
        {
            _viewModel.EnableVstPlugins = !_viewModel.EnableVstPlugins;
            SkiaCanvas.InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_renderer.HitTestMidiCheckbox(x, y))
        {
            _viewModel.EnableMidi = !_viewModel.EnableMidi;
            SkiaCanvas.InvalidateVisual();
            e.Handled = true;
            return;
        }

        var dropdownHit = _renderer.HitTestDropdownItem(x, y);
        if (dropdownHit.HasValue)
        {
            ApplyDropdownSelection(dropdownHit.Value);
            _uiState.ActiveDropdown = SettingsField.None;
            SkiaCanvas.InvalidateVisual();
            e.Handled = true;
            return;
        }

        var fieldHit = _renderer.HitTestField(x, y);
        if (fieldHit != SettingsField.None)
        {
            _uiState.ActiveDropdown = _uiState.ActiveDropdown == fieldHit ? SettingsField.None : fieldHit;
            _uiState.DropdownScroll = 0f;
            SkiaCanvas.InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_uiState.ActiveDropdown != SettingsField.None && !_renderer.HitTestDropdownList(x, y))
        {
            _uiState.ActiveDropdown = SettingsField.None;
            SkiaCanvas.InvalidateVisual();
            e.Handled = true;
        }
    }

    private void SkiaCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
    }

    private void SkiaCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_uiState.ActiveDropdown == SettingsField.None) return;

        float maxScroll = MathF.Max(0f, _renderer.DropdownContentHeight - _renderer.DropdownViewportHeight);
        float next = _uiState.DropdownScroll - e.Delta / 4f;
        _uiState.DropdownScroll = Math.Clamp(next, 0f, maxScroll);
        SkiaCanvas.InvalidateVisual();
        e.Handled = true;
    }

    private void ApplyDropdownSelection(DropdownItemHit hit)
    {
        switch (hit.Field)
        {
            case SettingsField.Input1:
                if ((uint)hit.Index < (uint)_viewModel.InputDevices.Count)
                    _viewModel.SelectedInputDevice1 = _viewModel.InputDevices[hit.Index];
                break;
            case SettingsField.Input2:
                if ((uint)hit.Index < (uint)_viewModel.InputDevices.Count)
                    _viewModel.SelectedInputDevice2 = _viewModel.InputDevices[hit.Index];
                break;
            case SettingsField.Output:
                if ((uint)hit.Index < (uint)_viewModel.OutputDevices.Count)
                    _viewModel.SelectedOutputDevice = _viewModel.OutputDevices[hit.Index];
                break;
            case SettingsField.Monitor:
                if ((uint)hit.Index < (uint)_viewModel.OutputDevices.Count)
                    _viewModel.SelectedMonitorDevice = _viewModel.OutputDevices[hit.Index];
                break;
            case SettingsField.SampleRate:
                if ((uint)hit.Index < (uint)_viewModel.SampleRateOptions.Count)
                    _viewModel.SelectedSampleRate = _viewModel.SampleRateOptions[hit.Index];
                break;
            case SettingsField.BufferSize:
                if ((uint)hit.Index < (uint)_viewModel.BufferSizeOptions.Count)
                    _viewModel.SelectedBufferSize = _viewModel.BufferSizeOptions[hit.Index];
                break;
            case SettingsField.Input1Channel:
                if ((uint)hit.Index < (uint)_viewModel.InputChannelOptions.Count)
                    _viewModel.SelectedInput1Channel = _viewModel.InputChannelOptions[hit.Index];
                break;
            case SettingsField.Input2Channel:
                if ((uint)hit.Index < (uint)_viewModel.InputChannelOptions.Count)
                    _viewModel.SelectedInput2Channel = _viewModel.InputChannelOptions[hit.Index];
                break;
            case SettingsField.OutputRouting:
                if ((uint)hit.Index < (uint)_viewModel.OutputRoutingOptions.Count)
                    _viewModel.SelectedOutputRouting = _viewModel.OutputRoutingOptions[hit.Index];
                break;
            case SettingsField.MidiDevice:
                if ((uint)hit.Index < (uint)_viewModel.MidiDevices.Count)
                    _viewModel.SelectedMidiDevice = _viewModel.MidiDevices[hit.Index];
                break;
        }
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
