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
        IReadOnlyList<AudioDevice> inputDevices,
        IReadOnlyList<AudioDevice> outputDevices,
        AudioDevice? selectedInput1,
        AudioDevice? selectedInput2,
        AudioDevice? selectedOutput,
        AudioDevice? selectedMonitor,
        int selectedSampleRate,
        int selectedBufferSize,
        InputChannelMode selectedInput1Channel,
        InputChannelMode selectedInput2Channel,
        OutputRoutingMode selectedOutputRouting)
    {
        InputDevices = new ObservableCollection<AudioDevice>(inputDevices);
        OutputDevices = new ObservableCollection<AudioDevice>(outputDevices);

        _selectedInputDevice1 = selectedInput1;
        _selectedInputDevice2 = selectedInput2;
        _selectedOutputDevice = selectedOutput;
        _selectedMonitorDevice = selectedMonitor;
        _selectedSampleRate = selectedSampleRate;
        _selectedBufferSize = selectedBufferSize;
        _selectedInput1Channel = selectedInput1Channel;
        _selectedInput2Channel = selectedInput2Channel;
        _selectedOutputRouting = selectedOutputRouting;
    }

    public ObservableCollection<AudioDevice> InputDevices { get; }
    public ObservableCollection<AudioDevice> OutputDevices { get; }

    public IReadOnlyList<int> SampleRateOptions { get; } = [44100, 48000];
    public IReadOnlyList<int> BufferSizeOptions { get; } = [128, 256, 512, 1024];
    public IReadOnlyList<InputChannelMode> InputChannelOptions { get; } = [InputChannelMode.Sum, InputChannelMode.Left, InputChannelMode.Right];
    public IReadOnlyList<OutputRoutingMode> OutputRoutingOptions { get; } = [OutputRoutingMode.Split, OutputRoutingMode.Sum];

    [ObservableProperty]
    private AudioDevice? _selectedInputDevice1;

    [ObservableProperty]
    private AudioDevice? _selectedInputDevice2;

    [ObservableProperty]
    private AudioDevice? _selectedOutputDevice;

    [ObservableProperty]
    private AudioDevice? _selectedMonitorDevice;

    [ObservableProperty]
    private int _selectedSampleRate;

    [ObservableProperty]
    private int _selectedBufferSize;

    [ObservableProperty]
    private InputChannelMode _selectedInput1Channel;

    [ObservableProperty]
    private InputChannelMode _selectedInput2Channel;

    [ObservableProperty]
    private OutputRoutingMode _selectedOutputRouting;

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
