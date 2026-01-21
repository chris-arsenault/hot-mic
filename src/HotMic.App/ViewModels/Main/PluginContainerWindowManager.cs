using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using HotMic.App.Views;

namespace HotMic.App.ViewModels;

internal sealed class PluginContainerWindowManager
{
    private readonly Dictionary<(int ChannelIndex, int ContainerId), PluginContainerWindow> _windows = new();

    public void OpenWindow(int channelIndex, int containerId, PluginContainerWindowViewModel viewModel)
    {
        var key = (channelIndex, containerId);
        if (_windows.TryGetValue(key, out var existing))
        {
            existing.Activate();
            return;
        }

        var window = new PluginContainerWindow(viewModel)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Closed += (_, _) => _windows.Remove(key);
        _windows[key] = window;
        window.Show();
    }

    public void CloseWindow(int channelIndex, int containerId)
    {
        var key = (channelIndex, containerId);
        if (_windows.TryGetValue(key, out var window))
        {
            _windows.Remove(key);
            window.Close();
        }
    }

    public void CloseAll()
    {
        if (_windows.Count == 0)
        {
            return;
        }

        foreach (var window in _windows.Values.ToArray())
        {
            window.Close();
        }

        _windows.Clear();
    }

    public bool TryGetViewModel(int channelIndex, int containerId, out PluginContainerWindowViewModel viewModel)
    {
        if (_windows.TryGetValue((channelIndex, containerId), out var window) && window.DataContext is PluginContainerWindowViewModel vm)
        {
            viewModel = vm;
            return true;
        }

        viewModel = null!;
        return false;
    }

    public IReadOnlyList<PluginContainerWindowEntry> GetWindowSnapshot()
    {
        if (_windows.Count == 0)
        {
            return Array.Empty<PluginContainerWindowEntry>();
        }

        var list = new List<PluginContainerWindowEntry>(_windows.Count);
        foreach (var entry in _windows)
        {
            if (entry.Value.DataContext is PluginContainerWindowViewModel viewModel)
            {
                list.Add(new PluginContainerWindowEntry(entry.Key.ChannelIndex, entry.Key.ContainerId, viewModel));
            }
        }

        return list;
    }

    public void UpdateMeterScale(bool voxScale)
    {
        foreach (var window in _windows.Values)
        {
            if (window.DataContext is PluginContainerWindowViewModel viewModel)
            {
                viewModel.MeterScaleVox = voxScale;
            }
        }
    }
}

internal readonly record struct PluginContainerWindowEntry(int ChannelIndex, int ContainerId, PluginContainerWindowViewModel ViewModel);
