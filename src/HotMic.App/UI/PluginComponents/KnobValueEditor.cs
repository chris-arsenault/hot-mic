using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using SkiaSharp;

// Explicitly use WPF types to avoid ambiguity with WinForms
using TextBox = System.Windows.Controls.TextBox;
using Border = System.Windows.Controls.Border;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using Thickness = System.Windows.Thickness;
using CornerRadius = System.Windows.CornerRadius;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Provides a popup textbox for entering knob values via right-click.
/// </summary>
public sealed class KnobValueEditor : IDisposable
{
    private readonly Popup _popup;
    private readonly TextBox _textBox;
    private readonly Border _border;
    private Action<float>? _onValueAccepted;
    private float _minValue;
    private float _maxValue;
    private string _unit = "";
    private bool _isLogScale;

    public KnobValueEditor()
    {
        _textBox = new TextBox
        {
            Width = 80,
            Height = 24,
            FontSize = 12,
            FontFamily = new FontFamily("Segoe UI"),
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 2, 4, 2),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _textBox.KeyDown += OnTextBoxKeyDown;
        _textBox.LostFocus += OnTextBoxLostFocus;

        _border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = _textBox
        };

        _popup = new Popup
        {
            Child = _border,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.None
        };
        _popup.Closed += OnPopupClosed;
    }

    /// <summary>
    /// Show the editor popup at the specified knob position.
    /// </summary>
    /// <param name="target">The visual element containing the knob (for positioning)</param>
    /// <param name="knobCenter">Center of the knob in logical coordinates</param>
    /// <param name="currentValue">Current DSP value to display</param>
    /// <param name="minValue">Minimum allowed value</param>
    /// <param name="maxValue">Maximum allowed value</param>
    /// <param name="unit">Unit string (e.g., "dB", "Hz", "%")</param>
    /// <param name="onValueAccepted">Callback when user enters a valid value</param>
    /// <param name="isLogScale">Whether the parameter uses logarithmic scaling</param>
    public void Show(
        UIElement target,
        SKPoint knobCenter,
        float currentValue,
        float minValue,
        float maxValue,
        string unit,
        Action<float> onValueAccepted,
        bool isLogScale = false)
    {
        _minValue = minValue;
        _maxValue = maxValue;
        _unit = unit;
        _onValueAccepted = onValueAccepted;
        _isLogScale = isLogScale;

        // Format the current value
        _textBox.Text = FormatValue(currentValue, unit);
        _textBox.SelectAll();

        // Position the popup at the knob
        _popup.PlacementTarget = target;
        _popup.Placement = PlacementMode.Relative;
        _popup.HorizontalOffset = knobCenter.X - 40; // Center the 80px wide popup
        _popup.VerticalOffset = knobCenter.Y - 12;   // Center vertically

        _popup.IsOpen = true;
        _textBox.Focus();
    }

    /// <summary>
    /// Check if the popup is currently open.
    /// </summary>
    public bool IsOpen => _popup.IsOpen;

    /// <summary>
    /// Close the popup without accepting the value.
    /// </summary>
    public void Cancel()
    {
        _popup.IsOpen = false;
        _onValueAccepted = null;
    }

    private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AcceptValue();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
    }

    private void OnTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        // Accept value when focus is lost (unless cancelled)
        if (_popup.IsOpen && _onValueAccepted != null)
        {
            AcceptValue();
        }
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        _onValueAccepted = null;
    }

    private void AcceptValue()
    {
        if (_onValueAccepted == null)
            return;

        string text = _textBox.Text.Trim();

        // Remove unit suffix if present
        if (!string.IsNullOrEmpty(_unit))
        {
            if (text.EndsWith(_unit, StringComparison.OrdinalIgnoreCase))
            {
                text = text[..^_unit.Length].Trim();
            }
            else if (_unit.Equals("Hz", StringComparison.OrdinalIgnoreCase))
            {
                // Handle "k" suffix for Hz (e.g., "1k" = 1000 Hz, "2.5k" = 2500 Hz)
                if (text.EndsWith("k", StringComparison.OrdinalIgnoreCase) ||
                    text.EndsWith("khz", StringComparison.OrdinalIgnoreCase))
                {
                    text = text.TrimEnd('k', 'K', 'h', 'H', 'z', 'Z').Trim();
                    if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float khzValue))
                    {
                        float hzValue = Math.Clamp(khzValue * 1000f, _minValue, _maxValue);
                        var callback = _onValueAccepted;
                        _popup.IsOpen = false;
                        callback?.Invoke(hzValue);
                        return;
                    }
                }
            }
        }

        // Parse the numeric value
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            value = Math.Clamp(value, _minValue, _maxValue);
            var callback = _onValueAccepted;
            _popup.IsOpen = false;
            callback?.Invoke(value);
        }
        else
        {
            // Invalid input - flash the border red briefly
            _border.BorderBrush = Brushes.Red;
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            timer.Tick += (_, _) =>
            {
                _border.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));
                timer.Stop();
            };
            timer.Start();
        }
    }

    private static string FormatValue(float value, string unit)
    {
        if (unit.Equals("Hz", StringComparison.OrdinalIgnoreCase))
        {
            if (value >= 1000)
                return $"{value / 1000f:0.0}k";
            return $"{value:0}";
        }
        if (unit.Equals("ms", StringComparison.OrdinalIgnoreCase))
        {
            return $"{value:0.0}";
        }
        if (unit.Equals("%", StringComparison.OrdinalIgnoreCase))
        {
            return $"{value:0}";
        }
        if (unit.Equals("dB", StringComparison.OrdinalIgnoreCase))
        {
            return $"{value:0.0}";
        }
        return $"{value:0.##}";
    }

    public void Dispose()
    {
        _textBox.KeyDown -= OnTextBoxKeyDown;
        _textBox.LostFocus -= OnTextBoxLostFocus;
        _popup.Closed -= OnPopupClosed;
    }
}
