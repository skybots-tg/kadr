using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Kadr.Core;

namespace Kadr.UI
{
    /// <summary>Screenshot pinned above all windows. Drag to move, wheel to zoom, Ctrl+wheel for opacity, Esc/double-click to close.</summary>
    public sealed class PinWindow : Window
    {
        const double Pad = 24;
        readonly PixelImage _img;
        readonly Image _pic;
        readonly Border _frame;
        readonly Border _zoomLabel;
        readonly TextBlock _zoomText;
        double _zoom = 1;
        Int32Rect? _placeAt;

        public PinWindow(PixelImage img, Int32Rect? screenRect)
        {
            _img = img;
            _placeAt = screenRect;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            Title = L.T("Кадр — закреплено", "Kadr — pinned");
            if (screenRect == null) WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _pic = new Image { Source = img.Bitmap, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(_pic, BitmapScalingMode.HighQuality);
            _frame = new Border
            {
                Child = _pic,
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 10, 132, 255)),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 5, Direction = 270, Opacity = 0.45 },
            };
            _zoomText = new TextBlock { Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold };
            _zoomLabel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 28, 28, 32)), CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 3, 8, 4),
                Child = _zoomText, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed, IsHitTestVisible = false,
            };
            var g = new Grid();
            g.Children.Add(_frame);
            g.Children.Add(_zoomLabel);
            Content = new Border { Padding = new Thickness(Pad), Child = g };
            ApplyZoom();

            MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2) { Close(); return; }
                try { DragMove(); } catch { }
            };
            MouseWheel += OnWheel;
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) Close();
                else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0) Output.CopyToClipboard(_img);
            };
            MouseEnter += (_, _) => _frame.BorderBrush = new SolidColorBrush(Color.FromArgb(220, 10, 132, 255));
            MouseLeave += (_, _) => _frame.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 10, 132, 255));

            var menu = new ContextMenu();
            menu.Items.Add(Item(L.T("Копировать", "Copy"), () => Output.CopyToClipboard(_img)));
            menu.Items.Add(Item(L.T("Сохранить как…", "Save as…"), () => Output.SaveAs(_img, this)));
            menu.Items.Add(Item(L.T("Исходный размер", "Actual size"), () => { _zoom = 1; Opacity = 1; ApplyZoom(); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(L.T("Закрыть", "Close"), Close));
            ContextMenu = menu;
        }

        static MenuItem Item(string text, Action a)
        {
            var m = new MenuItem { Header = text };
            m.Click += (_, _) => a();
            return m;
        }

        void ApplyZoom()
        {
            _pic.Width = _img.DipWidth * _zoom;
            _pic.Height = _img.DipHeight * _zoom;
        }

        System.Windows.Threading.DispatcherTimer _labelTimer;

        void OnWheel(object s, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                Opacity = Math.Clamp(Opacity + (e.Delta > 0 ? 0.1 : -0.1), 0.2, 1);
                ShowLabel($"{Math.Round(Opacity * 100)}%");
                return;
            }
            _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), 0.1, 6);
            ApplyZoom();
            ShowLabel($"{Math.Round(_zoom * 100)}%");
        }

        void ShowLabel(string text)
        {
            _zoomText.Text = text;
            _zoomLabel.Visibility = Visibility.Visible;
            _labelTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            _labelTimer.Stop();
            _labelTimer.Tick -= HideLabel;
            _labelTimer.Tick += HideLabel;
            _labelTimer.Start();
        }

        void HideLabel(object s, EventArgs e) { _labelTimer.Stop(); _zoomLabel.Visibility = Visibility.Collapsed; }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            if (_placeAt is Int32Rect r)
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int pad = (int)Math.Round(Pad * _img.Scale) + 1;
                Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, r.X - pad, r.Y - pad, 0, 0, 0x0001 /*NOSIZE*/ | Native.SWP_NOACTIVATE);
            }
        }
    }
}
