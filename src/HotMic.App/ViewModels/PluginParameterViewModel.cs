using CommunityToolkit.Mvvm.ComponentModel;

namespace HotMic.App.ViewModels;

public partial class PluginParameterViewModel : ObservableObject
{
    private readonly Action<float>? _onChange;
    private readonly Func<float, string>? _formatValue;

    public PluginParameterViewModel(int index, string name, float min, float max, float value, string unit, Action<float>? onChange, Func<float, string>? formatValue = null)
    {
        Index = index;
        Name = name;
        Min = min;
        Max = max;
        Unit = unit;
        _onChange = onChange;
        _formatValue = formatValue;
        Value = value;
    }

    public int Index { get; }

    public string Name { get; }

    public float Min { get; }

    public float Max { get; }

    public string Unit { get; }

    [ObservableProperty]
    private float value;

    public string DisplayValue => _formatValue is not null
        ? _formatValue(Value)
        : $"{Value:0.##} {Unit}".Trim();

    partial void OnValueChanged(float value)
    {
        OnPropertyChanged(nameof(DisplayValue));
        _onChange?.Invoke(value);
    }
}
