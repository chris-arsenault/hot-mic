namespace HotMic.Common.Configuration;

public sealed class UiConfig
{
    public string ViewMode { get; set; } = "full";
    public bool AlwaysOnTop { get; set; }
    public bool MeterScaleVox { get; set; }
    public bool MasterMeterLufs { get; set; }
    public WindowPositionConfig WindowPosition { get; set; } = new();
    public WindowSizeConfig WindowSize { get; set; } = new();
}

public sealed class WindowPositionConfig
{
    public double X { get; set; } = 100;
    public double Y { get; set; } = 100;
}

public sealed class WindowSizeConfig
{
    public double Width { get; set; } = 920;
    public double Height { get; set; } = 290;
}
