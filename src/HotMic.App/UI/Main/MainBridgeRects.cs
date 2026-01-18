using SkiaSharp;

namespace HotMic.App.UI;

internal sealed record CopyBridgeRect(int SourceChannelIndex, int TargetChannelIndex, SKRect SourceRect);
internal sealed record MergeBridgeRect(int SourceChannelIndex, int TargetChannelIndex, SKRect TargetRect);
