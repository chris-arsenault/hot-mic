using System;
using SkiaSharp;

namespace HotMic.App.UI;

internal sealed class MainMeterRenderer
{
    private static readonly float[] MeterGradientStops = [0f, 0.35f, 0.65f, 0.85f, 1f];
    private readonly MainPaintCache _paints;

    public MainMeterRenderer(MainPaintCache paints)
    {
        _paints = paints;
    }

    public void DrawVerticalMeter(SKCanvas canvas, float x, float y, float width, float height, float peakLevel, float rmsLevel, bool voxScale, float? voxTargetDb = null)
    {
        canvas.DrawRect(new SKRect(x, y, x + width, y + height), _paints.MeterBackgroundPaint);

        float rmsDb = LinearToDb(rmsLevel);
        float peakDb = LinearToDb(peakLevel);

        float rmsPos;
        float peakPos;
        if (voxScale)
        {
            rmsPos = DbToVoxMeterPosition(rmsDb);
            peakPos = DbToVoxMeterPosition(peakDb);
        }
        else
        {
            rmsPos = Math.Clamp((rmsDb + 60f) / 60f, 0f, 1f);
            peakPos = Math.Clamp((peakDb + 60f) / 60f, 0f, 1f);
        }

        float segmentHeight = (height - (MainLayoutMetrics.MeterSegments - 1) * MainLayoutMetrics.SegmentGap) / MainLayoutMetrics.MeterSegments;

        for (int i = 0; i < MainLayoutMetrics.MeterSegments; i++)
        {
            float segY = y + height - (i + 1) * (segmentHeight + MainLayoutMetrics.SegmentGap);
            float threshold = (i + 0.5f) / MainLayoutMetrics.MeterSegments;
            bool lit = rmsPos >= threshold;

            var segRect = new SKRect(x + 1f, segY, x + width - 1f, segY + segmentHeight);

            if (lit)
            {
                float segDb = voxScale ? VoxMeterPositionLinearToDb(threshold) : -60f + threshold * 60f;
                SKColor color = voxScale ? GetVoxMeterColor(segDb) : GetMeterSegmentColor(threshold);
                canvas.DrawRect(segRect, MainRenderPrimitives.CreateFillPaint(color));
            }
            else
            {
                canvas.DrawRect(segRect, _paints.MeterSegmentOffPaint);
            }
        }

        float peakY = y + height - height * peakPos;
        if (peakPos > 0.01f)
        {
            var peakColor = peakDb > -6f ? _paints.Theme.MeterClip : _paints.Theme.TextPrimary;
            canvas.DrawLine(x, peakY, x + width, peakY, MainRenderPrimitives.CreateStrokePaint(peakColor, 1.5f));
        }

        if (voxScale)
        {
            float targetDb = voxTargetDb ?? -18f;
            float targetPos = DbToVoxMeterPosition(targetDb);
            float targetY = y + height - height * targetPos;
            canvas.DrawLine(x, targetY, x + width, targetY, MainRenderPrimitives.CreateStrokePaint(_paints.Theme.Accent, 1f));
        }
    }

    public void DrawMiniMeter(SKCanvas canvas, float x, float y, float width, float height, float level, bool voxScale)
    {
        canvas.DrawRect(new SKRect(x, y, x + width, y + height), _paints.MeterBackgroundPaint);

        float db = LinearToDb(level);
        float pos = voxScale ? DbToVoxMeterPosition(db) : Math.Clamp((db + 60f) / 60f, 0f, 1f);

        if (pos > 0.01f)
        {
            float fillHeight = height * pos;
            var fillRect = new SKRect(x + 1f, y + height - fillHeight, x + width - 1f, y + height);

            SKColor color = voxScale ? GetVoxMeterColor(db) : GetMeterSegmentColor(pos);
            canvas.DrawRect(fillRect, MainRenderPrimitives.CreateFillPaint(color));
        }

        if (voxScale)
        {
            float targetPos = DbToVoxMeterPosition(-18f);
            float targetY = y + height - height * targetPos;
            canvas.DrawLine(x, targetY, x + width, targetY, MainRenderPrimitives.CreateStrokePaint(_paints.Theme.Accent, 1f));
        }
    }

    public void DrawHorizontalMeter(SKCanvas canvas, float x, float y, float width, float height, float peakLevel, float rmsLevel)
    {
        canvas.DrawRect(new SKRect(x, y, x + width, y + height), _paints.MeterBackgroundPaint);

        float rms = Math.Clamp(rmsLevel, 0f, 1f);
        float peak = Math.Clamp(peakLevel, 0f, 1f);

        if (rms > 0.01f)
        {
            float rmsWidth = width * rms;
            using var gradient = SKShader.CreateLinearGradient(
                new SKPoint(x, y),
                new SKPoint(x + width, y),
                new[] { _paints.Theme.MeterLow, _paints.Theme.MeterMid, _paints.Theme.MeterHigh, _paints.Theme.MeterWarn, _paints.Theme.MeterClip },
                MeterGradientStops,
                SKShaderTileMode.Clamp);

            using var gradientPaint = new SKPaint { Shader = gradient, IsAntialias = true };
            canvas.DrawRect(new SKRect(x + 1f, y + 1f, x + 1f + rmsWidth - 2f, y + height - 1f), gradientPaint);
        }

        float peakX = x + width * peak;
        if (peak > 0.01f)
        {
            var peakColor = peak >= 0.95f ? _paints.Theme.MeterClip : _paints.Theme.TextPrimary;
            canvas.DrawLine(peakX, y, peakX, y + height, MainRenderPrimitives.CreateStrokePaint(peakColor, 1.5f));
        }
    }

    public static float LinearToDb(float linear) => linear <= 0f ? -60f : 20f * MathF.Log10(linear + 1e-10f);

    public static float LufsToLinear(float lufs)
    {
        if (!float.IsFinite(lufs))
        {
            return 0f;
        }

        return MathF.Pow(10f, lufs / 20f);
    }

    private static float DbToVoxMeterPosition(float db)
    {
        db = Math.Clamp(db, -40f, 0f);

        if (db < -30f)
        {
            return (db + 40f) / 10f * 0.15f;
        }

        if (db < -12f)
        {
            return 0.15f + (db + 30f) / 18f * 0.60f;
        }

        return 0.75f + (db + 12f) / 12f * 0.25f;
    }

    private static float VoxMeterPositionLinearToDb(float pos)
    {
        pos = Math.Clamp(pos, 0f, 1f);

        if (pos < 0.15f)
        {
            return -40f + pos / 0.15f * 10f;
        }

        if (pos < 0.75f)
        {
            return -30f + (pos - 0.15f) / 0.60f * 18f;
        }

        return -12f + (pos - 0.75f) / 0.25f * 12f;
    }

    private static SKColor GetVoxMeterColor(float db)
    {
        if (db > -6f) return new SKColor(0xFF, 0x00, 0x00);
        if (db > -12f) return new SKColor(0xFF, 0xFF, 0x00);
        if (db > -30f) return new SKColor(0x00, 0xFF, 0x00);
        return new SKColor(0x66, 0x66, 0x66);
    }

    private SKColor GetMeterSegmentColor(float level)
    {
        if (level >= 0.95f) return _paints.Theme.MeterClip;
        if (level >= 0.85f) return _paints.Theme.MeterWarn;
        if (level >= 0.65f) return _paints.Theme.MeterHigh;
        if (level >= 0.35f) return _paints.Theme.MeterMid;
        return _paints.Theme.MeterLow;
    }
}
