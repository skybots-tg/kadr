using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Kadr.Core;
using Kadr.Editor;

namespace Kadr.UI
{
    /// <summary>Dark, Windows 11-styled window base: dark caption, app icon, shared palette.</summary>
    public class DarkWindow : Window
    {
        public static readonly Color Bg = Color.FromRgb(24, 24, 27);
        public static readonly Color CardColor = Color.FromRgb(34, 34, 38);
        public static readonly Brush TextMain = Theme.Brush(255, 240, 240, 243);
        public static readonly Brush TextDim = Theme.Brush(255, 150, 150, 158);

        public DarkWindow()
        {
            Background = new SolidColorBrush(Bg);
            Icon = App.AppIconImage;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            Foreground = TextMain;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            int dark = 1;
            Native.DwmSetWindowAttribute(hwnd, 20, ref dark, 4);            // immersive dark mode
            int caption = Bg.B << 16 | Bg.G << 8 | Bg.R;                     // COLORREF 0x00BBGGRR
            Native.DwmSetWindowAttribute(hwnd, 35, ref caption, 4);
        }

        public static TextBlock Text(string t, double size = 13, bool dim = false, FontWeight? weight = null) => new()
        {
            Text = t, FontSize = size, Foreground = dim ? TextDim : TextMain, FontWeight = weight ?? FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
        };
    }

    public sealed class ToggleSwitch : Border
    {
        readonly Border _knob;
        readonly TranslateTransform _tt = new();
        bool _on;
        public event Action<bool> Toggled;

        public ToggleSwitch(bool on)
        {
            Width = 40; Height = 22;
            CornerRadius = new CornerRadius(11);
            Cursor = Cursors.Hand;
            VerticalAlignment = VerticalAlignment.Center;
            _knob = new Border
            {
                Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0, 0, 0), RenderTransform = _tt,
            };
            Child = _knob;
            _on = on;
            Apply(false);
            MouseLeftButtonUp += (_, _) => { _on = !_on; Apply(true); Toggled?.Invoke(_on); };
        }

        public bool IsOn { get => _on; set { _on = value; Apply(false); } }

        void Apply(bool animate)
        {
            Background = new SolidColorBrush(_on ? AnnotationSurface.Accent : Color.FromRgb(72, 72, 78));
            double x = _on ? 18 : 0;
            if (animate) _tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase() });
            else { _tt.BeginAnimation(TranslateTransform.XProperty, null); _tt.X = x; }
        }
    }

    public sealed class PillButton : Border
    {
        readonly Brush _normal, _hover;
        public event Action Click;

        public PillButton(string text, bool primary, bool danger = false)
        {
            var baseColor = primary ? AnnotationSurface.Accent : danger ? Color.FromArgb(0, 0, 0, 0) : Color.FromArgb(22, 255, 255, 255);
            _normal = new SolidColorBrush(baseColor);
            _hover = new SolidColorBrush(primary ? Theme.AccentHover : danger ? Color.FromArgb(28, 255, 69, 58) : Color.FromArgb(38, 255, 255, 255));
            Background = _normal;
            CornerRadius = new CornerRadius(8);
            Padding = primary || danger ? new Thickness(16, 7, 16, 8) : new Thickness(12, 5, 12, 6);
            Cursor = Cursors.Hand;
            if (!primary && !danger) { BorderBrush = Theme.Hairline; BorderThickness = new Thickness(1); }
            Child = new TextBlock
            {
                Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = danger ? new SolidColorBrush(Color.FromRgb(255, 105, 97)) : Brushes.White,
            };
            MouseEnter += (_, _) => Background = _hover;
            MouseLeave += (_, _) => Background = _normal;
            MouseLeftButtonUp += (_, e) => { e.Handled = true; Click?.Invoke(); };
        }
    }

    public static class KeyCaps
    {
        /// <summary>"Ctrl" "Shift" "4" as separate little key caps.</summary>
        public static StackPanel Make(IEnumerable<string> parts, double size = 12)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var p in parts)
            {
                row.Children.Add(new Border
                {
                    Background = Theme.Brush(255, 52, 52, 58), CornerRadius = new CornerRadius(5), MinWidth = 22,
                    BorderBrush = Theme.Brush(255, 22, 22, 25), BorderThickness = new Thickness(0, 0, 0, 2),
                    Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 3, 0),
                    Child = new TextBlock { Text = p, FontSize = size, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center },
                });
            }
            return row;
        }
    }
}
