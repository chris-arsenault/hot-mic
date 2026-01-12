using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HotMic.App.Models;
using HotMic.App.UI;
using HotMic.App.ViewModels;
using HotMic.Common.Configuration;
using SkiaSharp;
using SkiaSharp.Views.WPF;

namespace HotMic.App;

public partial class MainWindow : Window
{
    private const float DragThreshold = 6f;

    private readonly MainRenderer _renderer = new();
    private readonly MainUiState _uiState = new();
    private readonly DispatcherTimer _renderTimer;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        LocationChanged += (_, _) => UpdateWindowPosition();
        SizeChanged += (_, _) => UpdateWindowSize();
        Closed += (_, _) => DisposeViewModel();

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += (_, _) => SkiaCanvas.InvalidateVisual();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _renderTimer.Start();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();
        _renderer.Render(canvas, size, viewModel, _uiState, dpiScale);
    }

    private void SkiaCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        var topButton = _renderer.HitTestTopButton(x, y);
        if (topButton.HasValue)
        {
            HandleTopButton(topButton.Value, viewModel);
            e.Handled = true;
            return;
        }

        if (_renderer.HitTestTitleBar(x, y))
        {
            DragMove();
            e.Handled = true;
            return;
        }

        if (viewModel.IsMinimalView)
        {
            return;
        }

        // Meter scale toggle in hotbar
        if (_renderer.HitTestMeterScaleToggle(x, y))
        {
            viewModel.MeterScaleVox = !viewModel.MeterScaleVox;
            e.Handled = true;
            return;
        }

        if (_renderer.HitTestQualityToggle(x, y))
        {
            viewModel.QualityMode = viewModel.QualityMode == HotMic.Common.Configuration.AudioQualityMode.LatencyPriority
                ? HotMic.Common.Configuration.AudioQualityMode.QualityPriority
                : HotMic.Common.Configuration.AudioQualityMode.LatencyPriority;
            e.Handled = true;
            return;
        }

        // Stats area click toggles debug overlay
        if (_renderer.HitTestStatsArea(x, y))
        {
            viewModel.ShowDebugOverlay = !viewModel.ShowDebugOverlay;
            e.Handled = true;
            return;
        }

        // Preset dropdown clicks
        int presetChannel = _renderer.HitTestPresetDropdown(x, y);
        if (presetChannel >= 0)
        {
            ShowPresetDropdownMenu(viewModel, presetChannel);
            e.Handled = true;
            return;
        }

        // Check plugin knobs first (they're on top of plugin slots)
        var pluginKnobHit = _renderer.HitTestPluginKnob(x, y);
        if (pluginKnobHit.HasValue)
        {
            StartPluginKnobDrag(viewModel, pluginKnobHit.Value, y);
            SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        var knobHit = _renderer.HitTestKnob(x, y);
        if (knobHit.HasValue)
        {
            StartKnobDrag(viewModel, knobHit.Value, y);
            SkiaCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        var toggleHit = _renderer.HitTestToggle(x, y);
        if (toggleHit.HasValue)
        {
            HandleToggle(viewModel, toggleHit.Value);
            e.Handled = true;
            return;
        }

        var pluginHit = _renderer.HitTestPluginSlot(x, y, out var region);
        if (pluginHit.HasValue)
        {
            HandlePluginSlotClick(viewModel, pluginHit.Value, region, x, y);
            e.Handled = true;
        }
    }

    private void SkiaCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        if (_uiState.KnobDrag is { } knobDrag && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateKnobDrag(viewModel, knobDrag, y);
            e.Handled = true;
        }

        if (_uiState.PluginDrag is { } pluginDrag && e.LeftButton == MouseButtonState.Pressed)
        {
            float dx = MathF.Abs(x - pluginDrag.StartX);
            float dy = MathF.Abs(y - pluginDrag.StartY);
            bool isDragging = pluginDrag.IsDragging || dx > DragThreshold || dy > DragThreshold;
            _uiState.PluginDrag = pluginDrag with { CurrentX = x, CurrentY = y, IsDragging = isDragging };
            e.Handled = true;
        }
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        SkiaCanvas.ReleaseMouseCapture();

        if (_uiState.KnobDrag is not null)
        {
            _uiState.KnobDrag = null;
            e.Handled = true;
        }

        if (_uiState.PluginDrag is { } pluginDrag)
        {
            var pos = e.GetPosition(SkiaCanvas);
            float x = (float)pos.X;
            float y = (float)pos.Y;

            if (pluginDrag.IsDragging)
            {
                var target = _renderer.HitTestPluginSlot(x, y, out _);
                if (target.HasValue && !(target.Value.ChannelIndex == pluginDrag.ChannelIndex && target.Value.SlotIndex == pluginDrag.SlotIndex))
                {
                    var channel = pluginDrag.ChannelIndex == 0 ? viewModel.Channel1 : viewModel.Channel2;
                    channel.MovePlugin(pluginDrag.SlotIndex, target.Value.SlotIndex);
                }
            }
            else
            {
                ExecutePluginAction(viewModel, pluginDrag.ChannelIndex, pluginDrag.SlotIndex);
            }

            _uiState.PluginDrag = null;
            e.Handled = true;
        }
    }

    private void SkiaCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // No longer used for device picker - reserved for future scroll functionality
    }

    private void SkiaCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.IsMinimalView)
        {
            return;
        }

        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        // Check plugin knobs first
        var pluginKnobHit = _renderer.HitTestPluginKnob(x, y);
        if (pluginKnobHit.HasValue)
        {
            ShowKnobContextMenu(viewModel, pluginKnobHit.Value, pos);
            e.Handled = true;
            return;
        }

        // Check regular knobs (input/output gain)
        var knobHit = _renderer.HitTestKnob(x, y);
        if (knobHit.HasValue)
        {
            ShowGainKnobContextMenu(viewModel, knobHit.Value, pos);
            e.Handled = true;
        }
    }

    private void ShowKnobContextMenu(MainViewModel viewModel, PluginKnobHit hit, System.Windows.Point position)
    {
        var channel = GetChannel(viewModel, hit.ChannelIndex);
        if ((uint)hit.SlotIndex >= (uint)channel.PluginSlots.Count)
        {
            return;
        }

        var slot = channel.PluginSlots[hit.SlotIndex];
        if (slot.ElevatedParams is null || slot.ElevatedParams.Length <= hit.ParamIndex)
        {
            return;
        }

        var paramDef = slot.ElevatedParams[hit.ParamIndex];
        string targetPath = $"channel{hit.ChannelIndex + 1}.plugin.{hit.SlotIndex}.{paramDef.Index}";
        string paramLabel = $"{slot.DisplayName} - {paramDef.Name}";

        ShowMidiContextMenu(viewModel, targetPath, paramLabel, paramDef.Min, paramDef.Max, position);
    }

    private void ShowGainKnobContextMenu(MainViewModel viewModel, KnobHit hit, System.Windows.Point position)
    {
        string targetPath = hit.KnobType == KnobType.InputGain
            ? $"channel{hit.ChannelIndex + 1}.inputGain"
            : $"channel{hit.ChannelIndex + 1}.outputGain";

        string label = hit.KnobType == KnobType.InputGain ? "Input Gain" : "Output Gain";
        string channelName = hit.ChannelIndex == 0 ? "Ch 1" : "Ch 2";

        ShowMidiContextMenu(viewModel, targetPath, $"{channelName} {label}", -60f, 12f, position);
    }

    private void ShowMidiContextMenu(MainViewModel viewModel, string targetPath, string label, float minValue, float maxValue, System.Windows.Point position)
    {
        var menu = new ContextMenu();

        var existingBinding = viewModel.GetMidiBinding(targetPath);

        // Header item (disabled, just for display)
        var headerItem = new MenuItem { Header = label, IsEnabled = false };
        menu.Items.Add(headerItem);
        menu.Items.Add(new Separator());

        if (existingBinding != null)
        {
            // Show current binding
            var currentItem = new MenuItem
            {
                Header = $"Bound to CC {existingBinding.CcNumber}" + (existingBinding.Channel.HasValue ? $" Ch {existingBinding.Channel}" : ""),
                IsEnabled = false
            };
            menu.Items.Add(currentItem);
            menu.Items.Add(new Separator());
        }

        // MIDI Learn item
        var learnItem = new MenuItem { Header = viewModel.IsMidiLearning ? "Cancel MIDI Learn" : "MIDI Learn" };
        learnItem.Click += (_, _) =>
        {
            if (viewModel.IsMidiLearning)
            {
                viewModel.CancelMidiLearn();
            }
            else
            {
                viewModel.StartMidiLearn(targetPath, (ccNumber, channel) =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        viewModel.AddMidiBinding(targetPath, ccNumber, channel, minValue, maxValue);
                    });
                });
            }
        };
        menu.Items.Add(learnItem);

        // Clear binding item (only if bound)
        if (existingBinding != null)
        {
            var clearItem = new MenuItem { Header = "Clear MIDI Binding" };
            clearItem.Click += (_, _) =>
            {
                viewModel.RemoveMidiBinding(targetPath);
            };
            menu.Items.Add(clearItem);
        }

        menu.PlacementTarget = SkiaCanvas;
        menu.IsOpen = true;
    }

    private void HandleTopButton(MainButton button, MainViewModel viewModel)
    {
        switch (button)
        {
            case MainButton.ToggleView:
                viewModel.ToggleViewCommand.Execute(null);
                break;
            case MainButton.Settings:
                viewModel.OpenSettingsCommand.Execute(null);
                break;
            case MainButton.Pin:
                viewModel.ToggleAlwaysOnTopCommand.Execute(null);
                break;
            case MainButton.Minimize:
                WindowState = WindowState.Minimized;
                break;
            case MainButton.Close:
                Close();
                break;
            case MainButton.SavePreset1:
                ShowSavePresetMenu(viewModel, 0);
                break;
            case MainButton.SavePreset2:
                ShowSavePresetMenu(viewModel, 1);
                break;
        }
    }

    private void HandlePluginSlotClick(MainViewModel viewModel, PluginSlotHit hit, PluginSlotRegion region, float x, float y)
    {
        var channel = hit.ChannelIndex == 0 ? viewModel.Channel1 : viewModel.Channel2;
        if (hit.SlotIndex >= channel.PluginSlots.Count)
        {
            return;
        }

        var slot = channel.PluginSlots[hit.SlotIndex];
        if (region == PluginSlotRegion.Bypass)
        {
            if (!slot.IsEmpty)
            {
                slot.IsBypassed = !slot.IsBypassed;
            }
            return;
        }

        if (region == PluginSlotRegion.Remove)
        {
            if (!slot.IsEmpty)
            {
                slot.RemoveCommand.Execute(null);
            }
            return;
        }

        if (slot.IsEmpty)
        {
            slot.ActionCommand.Execute(null);
            return;
        }

        _uiState.PluginDrag = new PluginDragState(hit.ChannelIndex, hit.SlotIndex, x, y, x, y, false);
    }

    private void ExecutePluginAction(MainViewModel viewModel, int channelIndex, int slotIndex)
    {
        var channel = channelIndex == 0 ? viewModel.Channel1 : viewModel.Channel2;
        if ((uint)slotIndex >= (uint)channel.PluginSlots.Count)
        {
            return;
        }

        channel.PluginSlots[slotIndex].ActionCommand.Execute(null);
    }

    private void StartKnobDrag(MainViewModel viewModel, KnobHit hit, float startY)
    {
        float startValue = hit.KnobType == KnobType.InputGain
            ? GetChannel(viewModel, hit.ChannelIndex).InputGainDb
            : GetChannel(viewModel, hit.ChannelIndex).OutputGainDb;

        _uiState.KnobDrag = new KnobDragState(hit.ChannelIndex, hit.KnobType, startValue, startY);
    }

    private void StartPluginKnobDrag(MainViewModel viewModel, PluginKnobHit hit, float startY)
    {
        var channel = GetChannel(viewModel, hit.ChannelIndex);
        if ((uint)hit.SlotIndex >= (uint)channel.PluginSlots.Count)
        {
            return;
        }

        var slot = channel.PluginSlots[hit.SlotIndex];
        float startValue = hit.ParamIndex == 0 ? slot.Param0Value : slot.Param1Value;
        var knobType = hit.ParamIndex == 0 ? KnobType.PluginParam0 : KnobType.PluginParam1;

        _uiState.KnobDrag = new KnobDragState(
            hit.ChannelIndex,
            knobType,
            startValue,
            startY,
            hit.SlotIndex,
            hit.MinValue,
            hit.MaxValue);
    }

    private static void UpdateKnobDrag(MainViewModel viewModel, KnobDragState drag, float currentY)
    {
        float delta = drag.StartY - currentY;
        float min = drag.MinValue;
        float max = drag.MaxValue;
        float range = max - min;
        float sensitivity = range / 150f;
        float nextValue = drag.StartValue + delta * sensitivity;
        nextValue = Math.Clamp(nextValue, min, max);

        var channel = GetChannel(viewModel, drag.ChannelIndex);

        switch (drag.KnobType)
        {
            case KnobType.InputGain:
                channel.InputGainDb = nextValue;
                break;
            case KnobType.OutputGain:
                channel.OutputGainDb = nextValue;
                break;
            case KnobType.PluginParam0:
                if ((uint)drag.PluginSlotIndex < (uint)channel.PluginSlots.Count)
                {
                    channel.PluginSlots[drag.PluginSlotIndex].Param0Value = nextValue;
                }
                break;
            case KnobType.PluginParam1:
                if ((uint)drag.PluginSlotIndex < (uint)channel.PluginSlots.Count)
                {
                    channel.PluginSlots[drag.PluginSlotIndex].Param1Value = nextValue;
                }
                break;
        }
    }

    private static void HandleToggle(MainViewModel viewModel, ToggleHit toggle)
    {
        switch (toggle.ToggleType)
        {
            case ToggleType.Mute:
                var channel = GetChannel(viewModel, toggle.ChannelIndex);
                channel.IsMuted = !channel.IsMuted;
                break;
            case ToggleType.Solo:
                channel = GetChannel(viewModel, toggle.ChannelIndex);
                channel.IsSoloed = !channel.IsSoloed;
                break;
            case ToggleType.InputStereo when toggle.ChannelIndex == 0:
                viewModel.Input1IsStereo = !viewModel.Input1IsStereo;
                break;
            case ToggleType.InputStereo when toggle.ChannelIndex == 1:
                viewModel.Input2IsStereo = !viewModel.Input2IsStereo;
                break;
            case ToggleType.MasterStereo:
                viewModel.MasterIsStereo = !viewModel.MasterIsStereo;
                break;
            case ToggleType.MasterMute:
                viewModel.MasterMuted = !viewModel.MasterMuted;
                break;
        }
    }

    private static ChannelStripViewModel GetChannel(MainViewModel viewModel, int channelIndex)
    {
        return channelIndex == 0 ? viewModel.Channel1 : viewModel.Channel2;
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }

    private void UpdateWindowPosition()
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.UpdateWindowPosition(Left, Top);
        }
    }

    private void UpdateWindowSize()
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.UpdateWindowSize(Width, Height);
        }
    }

    private void DisposeViewModel()
    {
        _renderTimer.Stop();
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Dispose();
        }
    }

    private void ShowPresetDropdownMenu(MainViewModel viewModel, int channelIndex)
    {
        var menu = new ContextMenu();
        var presetOptions = viewModel.GetPresetOptions();
        string currentPreset = channelIndex == 0 ? viewModel.Channel1PresetName : viewModel.Channel2PresetName;

        foreach (var presetName in presetOptions)
        {
            var item = new MenuItem
            {
                Header = presetName,
                IsCheckable = true,
                IsChecked = string.Equals(presetName, currentPreset, StringComparison.OrdinalIgnoreCase)
            };

            string capturedName = presetName;
            item.Click += (_, _) =>
            {
                viewModel.SelectChannelPreset(channelIndex, capturedName);
            };

            menu.Items.Add(item);
        }

        // Position menu below the dropdown
        var rect = _renderer.GetPresetDropdownRect(channelIndex);
        menu.PlacementTarget = SkiaCanvas;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
        menu.HorizontalOffset = rect.Left;
        menu.VerticalOffset = rect.Bottom;
        menu.IsOpen = true;
    }

    private void ShowSavePresetMenu(MainViewModel viewModel, int channelIndex)
    {
        var menu = new ContextMenu();
        string currentPreset = channelIndex == 0 ? viewModel.Channel1PresetName : viewModel.Channel2PresetName;

        // Save as new option
        var saveNewItem = new MenuItem { Header = "Save as New..." };
        saveNewItem.Click += (_, _) =>
        {
            ShowSavePresetDialog(viewModel, channelIndex, null);
        };
        menu.Items.Add(saveNewItem);

        // Overwrite current (only if current is a user preset that can be overwritten)
        if (viewModel.CanOverwritePreset(currentPreset))
        {
            var overwriteItem = new MenuItem { Header = $"Overwrite \"{currentPreset}\"" };
            overwriteItem.Click += (_, _) =>
            {
                viewModel.SaveCurrentAsPreset(channelIndex, currentPreset);
            };
            menu.Items.Add(overwriteItem);
        }

        menu.PlacementTarget = SkiaCanvas;
        menu.IsOpen = true;
    }

    private void ShowSavePresetDialog(MainViewModel viewModel, int channelIndex, string? suggestedName)
    {
        var dialog = new Views.InputDialog("Save Preset", "Enter preset name:", suggestedName ?? "My Preset")
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputValue))
        {
            string presetName = dialog.InputValue.Trim();

            // Check if this would overwrite a built-in preset
            if (!viewModel.CanOverwritePreset(presetName) &&
                !string.Equals(presetName, HotMic.Core.Presets.PluginPresetManager.CustomPresetName, StringComparison.OrdinalIgnoreCase))
            {
                // Check if it's a built-in preset name
                var options = viewModel.GetPresetOptions();
                if (options.Contains(presetName))
                {
                    System.Windows.MessageBox.Show($"Cannot overwrite built-in preset \"{presetName}\".", "Save Preset", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            viewModel.SaveCurrentAsPreset(channelIndex, presetName);
        }
    }
}
