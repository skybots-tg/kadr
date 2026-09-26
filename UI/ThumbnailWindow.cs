using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Kadr.Capture;
using Kadr.Core;
using Kadr.Editor;

namespace Kadr.UI
{
    /// <summary>macOS-style floating preview in the screen corner: drag it out, click to edit, swipe to dismiss.</summary>
    public sealed class ThumbnailWindow : Window
    {
        static readonly List<ThumbnailWindow> Open = new();
        const double EdgeMargin = 18, MaxW = 220, MaxH = 150, Pad = 20;

        readonly CaptureResult _r;
        readonly DispatcherTimer _timer;
        readonly Grid _card;
        readonly Grid _hoverLayer;
        readonly TranslateTransform _tt = new();
        Point _downScreen;
        bool _down, _dragging, _closing;
        double _slotY;

        public ThumbnailWindow(CaptureResult r)
        {
            _r = r;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            Title = "Кадр — миниатюра";

            var img = r.Image;
            double iw = img.Width / img.Scale, ih = img.Height / img.Scale;
            double k = Math.Min(MaxW / iw, MaxH / ih);
            double w = Math.Max(60, iw * k), h = Math.Max(40, ih * k);

            var picture = new Image { Source = img.Bitmap, Width = w, Height = h, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);

            _card = new Grid { Width = w, Height = h, RenderTransform = _tt, Cursor = Cursors.Hand };
            var frame = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = r.IsWindow ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                BorderBrush = r.IsWindow ? null : new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                BorderThickness = new Thickness(r.IsWindow ? 0 : 1),
                Effect = r.IsWindow ? null : new DropShadowEffect { BlurRadius = 22, ShadowDepth = 6, Direction = 270, Opacity = 0.45 },
            };
            var clip = new Grid { Clip = new RectangleGeometry(new Rect(0, 0, w, h), 10, 10) };
            clip.Children.Add(picture);
            frame.Child = clip;
            _card.Children.Add(frame);

            _hoverLayer = new Grid { Opacity = 0, IsHitTestVisible = true };
            _hoverLayer.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(r.IsWindow ? (byte)0 : (byte)70, 0, 0, 0)) });
            var center = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            center.Children.Add(Chip("Изменить", Icons.Edit, () => OpenEditor()));
            center.Children.Add(Chip("Копировать", Icons.Copy, () => { Output.CopyToClipboard(_r.Image); Flash("Скопировано"); }));
            _hoverLayer.Children.Add(center);
            _hoverLayer.Children.Add(Round(Icons.Close, HorizontalAlignment.Left, VerticalAlignment.Top, "Закрыть", () => Dismiss(true)));
            _hoverLayer.Children.Add(Round(Icons.Pin, HorizontalAlignment.Right, VerticalAlignment.Top, "Закрепить", () => { App.PinImage(_r.Image, null); Dismiss(false); }));
            _hoverLayer.Children.Add(Round(Icons.Folder, HorizontalAlignment.Right, VerticalAlignment.Bottom, "Показать в папке", () => { App.RevealInExplorer(EnsureFile()); Dismiss(false); }));
            _card.Children.Add(_hoverLayer);

            Content = new Border { Padding = new Thickness(Pad), Child = _card, Background = Brushes.Transparent };

            _card.MouseEnter += (_, _) => { _timer.Stop(); _hoverLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120))); };
            _card.MouseLeave += (_, _) => { if (!_down) _timer.Start(); _hoverLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(160))); };
            _card.MouseLeftButtonDown += OnDown;
            _card.MouseMove += OnMove;
            _card.MouseLeftButtonUp += OnUp;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            _timer.Tick += (_, _) => { _timer.Stop(); Dismiss(true); };
        }

        FrameworkElement Chip(string text, string icon, Action act)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(Icons.Make(icon, 13));
            sp.Children.Add(new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 0, 0, 1), FontFamily = KFonts.Family, VerticalAlignment = VerticalAlignment.Center });
            var b = new Border
            {
                Child = sp, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 5, 12, 6), Margin = new Thickness(0, 3, 0, 3),
                Background = new SolidColorBrush(Color.FromArgb(215, 36, 36, 40)), BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(AnnotationSurface.Accent);
            b.MouseLeave += (_, _) => b.Background = new SolidColorBrush(Color.FromArgb(215, 36, 36, 40));
            b.MouseLeftButtonDown += (_, e) => e.Handled = true;
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; act(); };
            return b;
        }

        FrameworkElement Round(string icon, HorizontalAlignment ha, VerticalAlignment va, string tip, Action act)
        {
            var b = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Child = Icons.Make(icon, 12, Brushes.White, 2.2),
                Background = new SolidColorBrush(Color.FromArgb(225, 36, 36, 40)), BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), BorderThickness = new Thickness(1),
                HorizontalAlignment = ha, VerticalAlignment = va, Margin = new Thickness(6), ToolTip = tip,
            };
            b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(AnnotationSurface.Accent);
            b.MouseLeave += (_, _) => b.Background = new SolidColorBrush(Color.FromArgb(225, 36, 36, 40));
            b.MouseLeftButtonDown += (_, e) => e.Handled = true;
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; act(); };
            return b;
        }

        void Flash(string text)
        {
            var t = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 52, 199, 89)), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 4, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
                Child = new TextBlock { Text = "✓ " + text, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 12, FontFamily = KFonts.Family },
            };
            _card.Children.Add(t);
            var a = new DoubleAnimationUsingKeyFrames();
            a.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            a.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900))));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1200))));
            a.Completed += (_, _) => _card.Children.Remove(t);
            t.BeginAnimation(OpacityProperty, a);
        }

        string EnsureFile()
        {
            if (_r.SavedPath == null || !System.IO.File.Exists(_r.SavedPath)) _r.SavedPath = Output.SaveTemp(_r.Image);
            return _r.SavedPath;
        }

        void OpenEditor()
        {
            Dismiss(false);
            var ed = new EditorWindow(_r);
            ed.Show();
            Native.ForceForeground(new WindowInteropHelper(ed).Handle);
            ed.Activate();
        }

        // ------------------------------------------------------------------ drag / swipe

        void OnDown(object s, MouseButtonEventArgs e)
        {
            _down = true;
            _dragging = false;
            _downScreen = PointToScreen(e.GetPosition(this));
            _card.CaptureMouse();
            _timer.Stop();
        }

        void OnMove(object s, MouseEventArgs e)
        {
            if (!_down) return;
            var now = PointToScreen(e.GetPosition(this));
            var d = now - _downScreen;
            var dpi = VisualTreeHelper.GetDpi(this);
            double dx = d.X / dpi.DpiScaleX, dy = d.Y / dpi.DpiScaleY;
            if (!_dragging && (Math.Abs(dx) > 6 || Math.Abs(dy) > 6))
            {
                if (dx > 0 && Math.Abs(dx) > Math.Abs(dy) * 1.2)
                {
                    _dragging = true; // swipe right to dismiss
                }
                else
                {
                    // drag the file out to another app
                    _down = false;
                    _card.ReleaseMouseCapture();
                    var data = new DataObject();
                    data.SetFileDropList(new System.Collections.Specialized.StringCollection { EnsureFile() });
                    data.SetImage(_r.Image.FlattenOn(Colors.White));
                    Opacity = 0.5;
                    try { DragDrop.DoDragDrop(_card, data, DragDropEffects.Copy | DragDropEffects.Move); } catch { }
                    Opacity = 1;
                    Dismiss(false);
                    return;
                }
            }
            if (_dragging) _tt.X = Math.Max(0, dx);
        }

        void OnUp(object s, MouseButtonEventArgs e)
        {
            if (!_down) return;
            _down = false;
            _card.ReleaseMouseCapture();
            if (_dragging)
            {
                _dragging = false;
                if (_tt.X > 60) Dismiss(true);
                else { _tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase() }); _timer.Start(); }
                return;
            }
            OpenEditor();
        }

        // ------------------------------------------------------------------ show / hide

        public void ShowAnimated()
        {
            // stack upward when several previews are visible
            Open.RemoveAll(t => t._closing);
            Open.Add(this);
            Show();
            var mon = _r.Monitor ?? ScreenCapture.MonitorUnderCursor(ScreenCapture.GetMonitors());
            var hwnd = new WindowInteropHelper(this).Handle;
            UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(this);
            int pw = (int)Math.Ceiling(ActualWidth * mon.Scale), ph = (int)Math.Ceiling(ActualHeight * mon.Scale);
            double stackOffset = 0;
            foreach (var t in Open)
            {
                if (t == this) break;
                stackOffset += t.ActualHeight * mon.Scale - Pad * mon.Scale;
            }
            var wa = mon.WorkArea;
            int x = wa.X + wa.Width - pw - (int)((EdgeMargin - Pad) * mon.Scale);
            int y = wa.Y + wa.Height - ph - (int)((EdgeMargin - Pad) * mon.Scale) - (int)stackOffset;
            _slotY = y;
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, pw, ph, Native.SWP_NOACTIVATE);

            _tt.X = ActualWidth;
            var ease = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut };
            _tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(ActualWidth, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
            _timer.Start();
        }

        public void Dismiss(bool animate)
        {
            if (_closing) return;
            _closing = true;
            _timer.Stop();
            Open.Remove(this);
            if (!animate) { Close(); return; }
            var a = new DoubleAnimation(_tt.X, ActualWidth + 20, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            a.Completed += (_, _) => Close();
            _tt.BeginAnimation(TranslateTransform.XProperty, a);
        }

        public static void DismissAll()
        {
            foreach (var t in Open.ToArray()) t.Dismiss(false);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            long ex = Native.GetExStyle(hwnd);
            SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW));
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    }
}
