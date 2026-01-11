using CommunityToolkit.Mvvm.ComponentModel;

namespace HotMic.App.ViewModels;

public partial class PluginParameterViewModel : ObservableObject
{
    private readonly Action<float>? _onChange;

    public PluginParameterViewModel(int index, string name, float min, float max, float value, string unit, Action<float>? onChange)
    {
        Index = index;
        Name = name;
        Min = min;
        Max = max;
        Unit = unit;
        _onChange = onChange;
        Value = value;
    }

    public int Index { get; }

    public string Name { get; }

    public float Min { get; }

    public float Max { get; }

    public string Unit { get; }

    [ObservableProperty]
    private float value;

    public string DisplayValue => $"{Value:0.##} {Unit}".Trim();

    partial void OnValueChanged(float value)
    {
        OnPropertyChanged(nameof(DisplayValue));
        _onChange?.Invoke(value);
    }
}
