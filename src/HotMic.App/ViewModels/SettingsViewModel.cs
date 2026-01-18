using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotMic.Common.Configuration;
using HotMic.Common.Models;

namespace HotMic.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    public event Action<bool>? CloseRequested;

    public SettingsViewModel(
        IReadOnlyList<AudioDevice> outputDevices,
        AudioDevice? selectedOutput,
        AudioDevice? selectedMonitor,
        int selectedSampleRate,
        int selectedBufferSize,
        bool enableVstPlugins = true,
        bool enableMidi = false,
        IReadOnlyList<string>? midiDevices = null,
        string? selectedMidiDevice = null)
    {
        OutputDevices = new ObservableCollection<AudioDevice>(outputDevices);
        MidiDevices = new ObservableCollection<string>(midiDevices ?? []);

        _selectedOutputDevice = selectedOutput;
        _selectedMonitorDevice = selectedMonitor;
        _selectedSampleRate = selectedSampleRate;
        _selectedBufferSize = selectedBufferSize;
        _enableVstPlugins = enableVstPlugins;
        _enableMidi = enableMidi;
        _selectedMidiDevice = selectedMidiDevice;
    }

    public ObservableCollection<AudioDevice> OutputDevices { get; }
    public ObservableCollection<string> MidiDevices { get; }

    public IReadOnlyList<int> SampleRateOptions { get; } = [44100, 48000];
    public IReadOnlyList<int> BufferSizeOptions { get; } = [128, 256, 512, 1024];

    [ObservableProperty]
    private AudioDevice? _selectedOutputDevice;

    [ObservableProperty]
    private AudioDevice? _selectedMonitorDevice;

    [ObservableProperty]
    private int _selectedSampleRate;

    [ObservableProperty]
    private int _selectedBufferSize;

    [ObservableProperty]
    private bool _enableVstPlugins;

    [ObservableProperty]
    private bool _enableMidi;

    [ObservableProperty]
    private string? _selectedMidiDevice;

    [RelayCommand]
    private void Apply()
    {
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(false);
    }
}
