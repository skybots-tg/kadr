using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Kadr.Core;
using Kadr.Editor;
using Kadr.UI;

namespace Kadr.Capture
{
    /// <summary>Frozen-screen overlay for one monitor: region/window selection and in-place markup.</summary>
    public sealed class OverlayWindow : Window
    {
        enum St { Idle, Selecting, Editing }
        enum EDrag { None, Resize, MoveSel, Surface }

        readonly CaptureSession _session;
        public readonly MonitorInfo Monitor;
        readonly PixelImage _img;
        readonly double _s;
        IntPtr _hwnd;

        readonly Grid _root = new();
        readonly Path _dim;
        readonly RectangleGeometry _dimHole = new();
        readonly AnnotationSurface _surface = new();
        readonly Canvas _chrome = new();
        readonly Rectangle _selBorder, _selShade;
        readonly Ellipse[] _handles = new Ellipse[8];
        readonly Border _sizeLabel;
        readonly TextBlock _sizeText;
        readonly Rectangle _hoverRect;
        readonly Border _hoverLabel;
        readonly TextBlock _hoverTitle, _hoverSize;
        readonly Loupe _loupe;
        readonly Toolbar _toolbar;
        readonly Border _hint;
        readonly StackPanel _hintRow;

        St _st = St.Idle;
        EDrag _edrag = EDrag.None;
        Rect _sel;              // selection in monitor pixels
        Point _startPx, _curPx, _lastPx;
        Rect _selAtStart;
        int _resizeHandle;
        bool _spaceHeld, _closing, _toolbarShown;
        WindowInfo _hoverWin;

        public OverlayWindow(CaptureSession session, MonitorInfo monitor, PixelImage img)
        {
            _session = session;
            Monitor = monitor;
            _img = img;
            _s = monitor.Scale;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Background = Brushes.Black;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = monitor.Bounds.X / _s; Top = monitor.Bounds.Y / _s;
            Width = monitor.Bounds.Width / _s; Height = monitor.Bounds.Height / _s;
            UseLayoutRounding = false;
            FocusVisualStyle = null;
            Title = L.AppName;

            var bg = new Image { Source = img.Bitmap, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(bg, BitmapScalingMode.NearestNeighbor);
            _root.Children.Add(bg);

            var full = new RectangleGeometry(new Rect(0, 0, monitor.Bounds.Width / _s, monitor.Bounds.Height / _s));
            _dim = new Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(92, 0, 0, 0)),
                Data = new GeometryGroup { FillRule = FillRule.EvenOdd, Children = { full, _dimHole } },
                IsHitTestVisible = false,
                Opacity = 0,
            };
            _root.Children.Add(_dim);

            _surface.Env = new RenderEnv { Base = img, BaseOrigin = new Point(0, 0) };
            _surface.IsHitTestVisible = true;
            _root.Children.Add(_surface);

            _root.Children.Add(_chrome);

            _hoverRect = new Rectangle { IsHitTestVisible = false, Visibility = Visibility.Collapsed, RadiusX = 8, RadiusY = 8 };
            _chrome.Children.Add(_hoverRect);
            _hoverTitle = new TextBlock { Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, FontFamily = KFonts.Family, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Center };
            _hoverSize = new TextBlock { Foreground = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)), FontSize = 11.5, FontFamily = KFonts.Family, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 0) };
            _hoverLabel = Pill(new StackPanel { Children = { _hoverTitle, _hoverSize } }, new Thickness(12, 6, 12, 7));
            _hoverLabel.Visibility = Visibility.Collapsed;
            _chrome.Children.Add(_hoverLabel);

            _selShade = new Rectangle { Stroke = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), StrokeThickness = 1, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            _selBorder = new Rectangle { Stroke = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)), StrokeThickness = 1, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            _chrome.Children.Add(_selShade);
            _chrome.Children.Add(_selBorder);
            for (int i = 0; i < 8; i++)
            {
                _handles[i] = new Ellipse
                {
                    Width = 8, Height = 8, Fill = Brushes.White, Stroke = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), StrokeThickness = 1,
                    IsHitTestVisible = false, Visibility = Visibility.Collapsed,
                    Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 0.5, Direction = 270, Opacity = 0.35 },
                };
                _chrome.Children.Add(_handles[i]);
            }

            _sizeText = new TextBlock { Foreground = Brushes.White, FontSize = 11.5, FontFamily = KFonts.Family, FontWeight = FontWeights.SemiBold };
            _sizeLabel = Pill(_sizeText, new Thickness(7, 2, 7, 3));
            _sizeLabel.Visibility = Visibility.Collapsed;
            _chrome.Children.Add(_sizeLabel);

            _loupe = new Loupe(img) { Visibility = Visibility.Collapsed };
            _chrome.Children.Add(_loupe);

            _hintRow = new StackPanel { Orientation = Orientation.Horizontal };
            _hint = Pill(_hintRow, new Thickness(12, 7, 12, 7));
            _hint.CornerRadius = new CornerRadius(12);
            _hint.Visibility = Visibility.Collapsed;
            _hint.IsHitTestVisible = false;
            _chrome.Children.Add(_hint);

            _toolbar = new Toolbar(_surface);
            _toolbar.Main.Visibility = Visibility.Collapsed;
            _chrome.Children.Add(_toolbar.Main);
            _chrome.Children.Add(_toolbar.Options);
            _toolbar.CopyClicked += () => Finish(FinishAction.Copy);
            _toolbar.SaveClicked += () => Finish(FinishAction.Save);
            _toolbar.PinClicked += () => Finish(FinishAction.Pin);
            _toolbar.CloseClicked += () => _session.Cancel();
            _toolbar.OptionsChanged += PlaceToolbar;
            _surface.StateChanged += UpdateCursor;

            Content = _root;
            UpdateCursor();

            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;
            MouseRightButtonDown += (_, e) => { if (_st != St.Editing) _session.Cancel(); };
            MouseLeave += (_, _) => { if (_st == St.Idle) { _loupe.Visibility = Visibility.Collapsed; _hoverRect.Visibility = _hoverLabel.Visibility = Visibility.Collapsed; } };
            PreviewKeyDown += OnKeyDown;
            PreviewKeyUp += OnKeyUp;
            Loaded += (_, _) =>
            {
                ApplyBounds();
                _dim.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
            };
            Deactivated += (_, _) => { _spaceHeld = false; };
        }

        static Border Pill(UIElement child, Thickness pad) => new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(232, 30, 30, 34)),
            CornerRadius = new CornerRadius(8),
            Padding = pad,
            Child = child,
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Direction = 270, Opacity = 0.35 },
        };

        // ------------------------------------------------------------------ window plumbing

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
            int v = 1;
            Native.DwmSetWindowAttribute(_hwnd, 3 /* DWMWA_TRANSITIONS_FORCEDISABLED */, ref v, 4);
            int corner = 1; // DWMWCP_DONOTROUND
            Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
            ApplyBounds();
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            ApplyBounds();
        }

        void ApplyBounds()
        {
            if (_hwnd == IntPtr.Zero) return;
            var b = Monitor.Bounds;
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, Native.SWP_NOACTIVATE);
        }

        public void ActivateHard()
        {
            Native.ForceForeground(_hwnd);
            Activate();
            Focus();
            Keyboard.Focus(this);
            ShowHint();
            if (Native.GetCursorPos(out var p))
            {
                var local = new Point((p.X - Monitor.Bounds.X) / _s, (p.Y - Monitor.Bounds.Y) / _s);
                UpdateIdleHover(local);
            }
        }

        public void CloseQuietly()
        {
            if (_closing) return;
            _closing = true;
            Close();
        }

        // ------------------------------------------------------------------ geometry helpers

        double MonW => Monitor.Bounds.Width;
        double MonH => Monitor.Bounds.Height;

        Point ToPx(Point dip) => new Point(Math.Clamp(Math.Round(dip.X * _s), 0, MonW), Math.Clamp(Math.Round(dip.Y * _s), 0, MonH));
        Rect ToDip(Rect px) => new Rect(px.X / _s, px.Y / _s, px.Width / _s, px.Height / _s);
        Rect SelDip => ToDip(_sel);

        Int32Rect SelInt()
        {
            int x = (int)Math.Round(_sel.X), y = (int)Math.Round(_sel.Y);
            int w = Math.Max(1, (int)Math.Round(_sel.Width)), h = Math.Max(1, (int)Math.Round(_sel.Height));
            return new Int32Rect(x, y, w, h);
        }

        Rect ClampToMonitor(Rect r)
        {
            double x = Math.Clamp(r.X, 0, MonW), y = Math.Clamp(r.Y, 0, MonH);
            double x2 = Math.Clamp(r.Right, 0, MonW), y2 = Math.Clamp(r.Bottom, 0, MonH);
            return new Rect(x, y, Math.Max(0, x2 - x), Math.Max(0, y2 - y));
        }

        // ------------------------------------------------------------------ state

        public void OnModeChanged()
        {
            if (_st != St.Idle) return;
            UpdateCursor();
            _hoverWin = null;
            if (IsMouseOver) UpdateIdleHover(Mouse.GetPosition(_root));
            if (IsActive) ShowHint();
        }

        public void ResetToIdle()
        {
            _st = St.Idle;
            _edrag = EDrag.None;
            _sel = Rect.Empty;
            _dimHole.Rect = Rect.Empty;
            _surface.Tool = Tool.None;
            HideSelectionChrome();
            UpdateCursor();
        }

        void HideSelectionChrome()
        {
            _selBorder.Visibility = Visibility.Collapsed;
            _selShade.Visibility = Visibility.Collapsed;
            foreach (var h in _handles) h.Visibility = Visibility.Collapsed;
            _sizeLabel.Visibility = Visibility.Collapsed;
            _toolbar.Main.Visibility = Visibility.Collapsed;
            _toolbar.Options.Visibility = Visibility.Collapsed;
            _toolbarShown = false;
        }

        void ShowHint()
        {
            _hintRow.Children.Clear();
            var parts = _session.WindowMode
                ? new[] { (L.T("Клик", "Click"), L.T("снимок окна", "capture window")), ("Alt", L.T("без тени", "no shadow")), (L.T("Пробел", "Space"), L.T("область", "region")), ("Esc", L.T("отмена", "cancel")) }
                : new[] { (L.T("Тяните", "Drag"), L.T("область", "region")), (L.T("Клик", "Click"), L.T("окно", "window")), (L.T("Пробел", "Space"), L.T("снимок окна", "window shot")), ("Esc", L.T("отмена", "cancel")) };
            for (int i = 0; i < parts.Length; i++)
            {
                var (k, what) = parts[i];
                _hintRow.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)), CornerRadius = new CornerRadius(5),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), BorderThickness = new Thickness(1),
                    Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(i == 0 ? 0 : 14, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = k, Foreground = Brushes.White, FontSize = 11.5, FontWeight = FontWeights.SemiBold, FontFamily = KFonts.Family },
                });
                _hintRow.Children.Add(new TextBlock { Text = what, Foreground = new SolidColorBrush(Color.FromArgb(205, 255, 255, 255)), FontSize = 12.5, FontFamily = KFonts.Family, VerticalAlignment = VerticalAlignment.Center });
            }
            _hint.Visibility = Visibility.Visible;
            _hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(_hint, (MonW / _s - _hint.DesiredSize.Width) / 2);
            Canvas.SetTop(_hint, 28);
            var fade = new DoubleAnimationUsingKeyFrames();
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(3200))));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(3700))));
            _hint.BeginAnimation(OpacityProperty, fade);
        }

        void HideHint()
        {
            _hint.BeginAnimation(OpacityProperty, null);
            _hint.Visibility = Visibility.Collapsed;
        }

        void UpdateCursor()
        {
            if (_st == St.Idle || _st == St.Selecting)
            {
                _root.Cursor = _session.WindowMode && _st == St.Idle ? CursorFactory.Camera(_s) : CursorFactory.Crosshair(_s);
                return;
            }
            if (_edrag != EDrag.None) return;
            var p = Mouse.GetPosition(_root);
            _root.Cursor = EditCursorAt(p);
        }

        Cursor EditCursorAt(Point p)
        {
            var sc = _surface.CursorAt(p);
            if (sc != null) return sc;
            int h = HitSelHandle(p);
            if (h >= 0) return h switch { 0 or 4 => Cursors.SizeNWSE, 2 or 6 => Cursors.SizeNESW, 1 or 5 => Cursors.SizeNS, _ => Cursors.SizeWE };
            bool inside = SelDip.Contains(p);
            if (inside && _surface.Tool != Tool.None) return CursorFactory.Crosshair(_s);
            if (inside) return Cursors.SizeAll;
            return _surface.HasContent ? Cursors.Arrow : CursorFactory.Crosshair(_s);
        }

        // ------------------------------------------------------------------ idle hover / loupe

        void UpdateIdleHover(Point p)
        {
            var px = ToPx(p);
            if (_session.WindowMode)
            {
                _loupe.Visibility = Visibility.Collapsed;
                var w = _session.WindowAt(Monitor.Bounds.X + (int)px.X, Monitor.Bounds.Y + (int)px.Y);
                if (w != _hoverWin || _hoverRect.Visibility != Visibility.Visible) ShowWindowHover(w, true);
                return;
            }

            if (Settings.Current.ShowMagnifier) ShowLoupe(p);
            var win = _session.WindowAt(Monitor.Bounds.X + (int)px.X, Monitor.Bounds.Y + (int)px.Y);
            if (win != _hoverWin || _hoverRect.Visibility != Visibility.Visible) ShowWindowHover(win, false);
        }

        void ShowWindowHover(WindowInfo w, bool strong)
        {
            _hoverWin = w;
            if (w == null)
            {
                _hoverRect.Visibility = Visibility.Collapsed;
                _hoverLabel.Visibility = Visibility.Collapsed;
                return;
            }
            var r = new Rect(w.Bounds.X - Monitor.Bounds.X, w.Bounds.Y - Monitor.Bounds.Y, w.Bounds.Width, w.Bounds.Height);
            r = ToDip(ClampToMonitor(r));
            Canvas.SetLeft(_hoverRect, r.X); Canvas.SetTop(_hoverRect, r.Y);
            _hoverRect.Width = r.Width; _hoverRect.Height = r.Height;
            if (strong)
            {
                _hoverRect.Fill = new SolidColorBrush(Color.FromArgb(70, 10, 132, 255));
                _hoverRect.Stroke = new SolidColorBrush(Color.FromArgb(220, 10, 132, 255));
                _hoverRect.StrokeThickness = 2;
                _hoverRect.StrokeDashArray = null;
                _hoverTitle.Text = string.IsNullOrEmpty(w.Title) ? L.T("Окно", "Window") : w.Title;
                _hoverSize.Text = $"{w.Bounds.Width} × {w.Bounds.Height}";
                _hoverLabel.Visibility = Visibility.Visible;
                _hoverLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var d = _hoverLabel.DesiredSize;
                Canvas.SetLeft(_hoverLabel, r.X + (r.Width - d.Width) / 2);
                Canvas.SetTop(_hoverLabel, r.Y + (r.Height - d.Height) / 2);
            }
            else
            {
                _hoverRect.Fill = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255));
                _hoverRect.Stroke = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255));
                _hoverRect.StrokeThickness = 1;
                _hoverRect.StrokeDashArray = null;
                _hoverLabel.Visibility = Visibility.Collapsed;
            }
            _hoverRect.Visibility = Visibility.Visible;
        }

        void ShowLoupe(Point p)
        {
            var px = ToPx(p);
            _loupe.Update((int)Math.Min(px.X, MonW - 1), (int)Math.Min(px.Y, MonH - 1));
            _loupe.Visibility = Visibility.Visible;
            double w = Loupe.Size, h = Loupe.Size + 34;
            double x = p.X + 26, y = p.Y + 26;
            if (x + w > MonW / _s - 6) x = p.X - 26 - w;
            if (y + h > MonH / _s - 6) y = p.Y - 26 - h;
            Canvas.SetLeft(_loupe, x);
            Canvas.SetTop(_loupe, y);
        }

        // ------------------------------------------------------------------ selection chrome

        Point[] HandlePoints(Rect r) => new[]
        {
            r.TopLeft, new Point(r.X + r.Width / 2, r.Y), r.TopRight, new Point(r.Right, r.Y + r.Height / 2),
            r.BottomRight, new Point(r.X + r.Width / 2, r.Bottom), r.BottomLeft, new Point(r.X, r.Y + r.Height / 2),
        };

        int HitSelHandle(Point p)
        {
            if (_st != St.Editing) return -1;
            var r = SelDip;
            var hs = HandlePoints(r);
            for (int i = 0; i < 8; i++)
                if ((hs[i] - p).Length <= 9) return i;
            // edges
            const double t = 4;
            bool inY = p.Y > r.Top && p.Y < r.Bottom, inX = p.X > r.Left && p.X < r.Right;
            if (inY && Math.Abs(p.X - r.Left) <= t) return 7;
            if (inY && Math.Abs(p.X - r.Right) <= t) return 3;
            if (inX && Math.Abs(p.Y - r.Top) <= t) return 1;
            if (inX && Math.Abs(p.Y - r.Bottom) <= t) return 5;
            return -1;
        }

        void UpdateSelectionVisuals()
        {
            var d = SelDip;
            _dimHole.Rect = d;
            double px1 = 1 / _s;
            _selBorder.Visibility = Visibility.Visible;
            _selBorder.StrokeThickness = px1;
            Canvas.SetLeft(_selBorder, d.X - px1); Canvas.SetTop(_selBorder, d.Y - px1);
            _selBorder.Width = d.Width + 2 * px1; _selBorder.Height = d.Height + 2 * px1;
            _selShade.Visibility = Visibility.Visible;
            _selShade.StrokeThickness = px1;
            Canvas.SetLeft(_selShade, d.X - 2 * px1); Canvas.SetTop(_selShade, d.Y - 2 * px1);
            _selShade.Width = d.Width + 4 * px1; _selShade.Height = d.Height + 4 * px1;

            bool showHandles = _st == St.Editing;
            var hs = HandlePoints(d);
            for (int i = 0; i < 8; i++)
            {
                bool vis = showHandles && (i % 2 == 0 || (i % 4 == 1 ? d.Width > 60 : d.Height > 60));
                _handles[i].Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
                Canvas.SetLeft(_handles[i], hs[i].X - 4); Canvas.SetTop(_handles[i], hs[i].Y - 4);
            }

            _sizeText.Text = $"{(int)Math.Round(_sel.Width)} × {(int)Math.Round(_sel.Height)}";
            _sizeLabel.Visibility = Visibility.Visible;
            _sizeLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var ls = _sizeLabel.DesiredSize;
            double lx = d.X, ly = d.Y - ls.Height - 6;
            if (ly < 4) ly = d.Y + 6 >= 0 ? d.Y + 6 : 4;
            if (ly < 4) { lx = d.X + 8; }
            lx = Math.Clamp(lx, 4, MonW / _s - ls.Width - 4);
            Canvas.SetLeft(_sizeLabel, lx); Canvas.SetTop(_sizeLabel, ly);

            _surface.Clip = new RectangleGeometry(d);
            if (_st == St.Editing) PlaceToolbar();
        }

        void PlaceToolbar()
        {
            if (_st != St.Editing) return;
            var main = _toolbar.Main;
            if (!_toolbarShown)
            {
                _toolbarShown = true;
                main.Visibility = Visibility.Visible;
                main.Opacity = 0;
                var tt = new TranslateTransform(0, 8);
                main.RenderTransform = tt;
                var ease = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut };
                main.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            }
            main.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var sz = main.DesiredSize;
            double W = MonW / _s, H = MonH / _s;
            var d = SelDip;
            const double gap = 12, edge = 10;

            double x = d.X + d.Width / 2 - sz.Width / 2;
            x = Math.Clamp(x, edge, Math.Max(edge, W - sz.Width - edge));
            double y;
            bool below;
            if (d.Bottom + gap + sz.Height <= H - edge) { y = d.Bottom + gap; below = true; }
            else if (d.Top - gap - sz.Height >= edge) { y = d.Top - gap - sz.Height; below = false; }
            else { y = d.Bottom - gap - sz.Height; below = false; y = Math.Max(edge, y); }
            Canvas.SetLeft(main, x); Canvas.SetTop(main, y);
            main.SetBackdrop(_img.Bitmap, new Rect(x * _s, y * _s, sz.Width * _s, sz.Height * _s), _s);

            var opt = _toolbar.Options;
            if (opt.Visibility == Visibility.Visible)
            {
                opt.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var os = opt.DesiredSize;
                double ox = Math.Clamp(x + sz.Width / 2 - os.Width / 2, edge, Math.Max(edge, W - os.Width - edge));
                // away from the selection when there is room, otherwise the other side of the bar
                double oy = below ? y + sz.Height + 6 : y - 6 - os.Height;
                if (below && oy + os.Height > H - edge) oy = y - 6 - os.Height;
                if (!below && oy < edge) oy = y + sz.Height + 6;
                Canvas.SetLeft(opt, ox); Canvas.SetTop(opt, oy);
                opt.SetBackdrop(_img.Bitmap, new Rect(ox * _s, oy * _s, os.Width * _s, os.Height * _s), _s);
            }
        }

        bool OverToolbar(object src)
        {
            for (var d = src as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
                if (d == _toolbar.Main || d == _toolbar.Options) return true;
            return false;
        }

        // ------------------------------------------------------------------ pointer

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (OverToolbar(e.OriginalSource)) return;
            var p = e.GetPosition(_root);
            var px = ToPx(p);
            HideHint();

            if (_st == St.Idle)
            {
                if (_session.WindowMode)
                {
                    var w = _session.WindowAt(Monitor.Bounds.X + (int)px.X, Monitor.Bounds.Y + (int)px.Y);
                    if (w != null) _session.CaptureWindow(w, Settings.Current.WindowShadow && (Keyboard.Modifiers & ModifierKeys.Alt) == 0);
                    return;
                }
                _session.BeginSelection(this);
                _st = St.Selecting;
                _startPx = _curPx = _lastPx = px;
                _sel = new Rect(px, px);
                _root.CaptureMouse();
                return;
            }

            if (_st != St.Editing) return;

            // annotation handles take priority, then selection handles, then drawing
            bool inside = SelDip.Contains(p);
            int sh = HitSelHandle(p);
            bool surfaceWants = _surface.WantsPointer(p) && (inside || _surface.CursorAt(p) != null);
            if (sh >= 0 && _surface.CursorAt(p) == null && !_surface.IsEditingText)
            {
                _edrag = EDrag.Resize;
                _resizeHandle = sh;
                _selAtStart = _sel;
                _startPx = px;
                _root.CaptureMouse();
                return;
            }
            if (surfaceWants || _surface.IsEditingText)
            {
                if (_surface.OnDown(p, Keyboard.Modifiers, e.ClickCount))
                {
                    _edrag = EDrag.Surface;
                    if (!_surface.IsEditingText) _root.CaptureMouse();
                    return;
                }
                if (_surface.IsEditingText) return;
            }
            if (inside)
            {
                _edrag = EDrag.MoveSel;
                _selAtStart = _sel;
                _startPx = px;
                _root.CaptureMouse();
                return;
            }
            if (!_surface.HasContent)
            {
                // start over with a fresh selection
                _st = St.Selecting;
                HideSelectionChrome();
                _startPx = _curPx = _lastPx = px;
                _sel = new Rect(px, px);
                _dimHole.Rect = Rect.Empty;
                _root.CaptureMouse();
                UpdateCursor();
            }
        }

        void OnMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(_root);
            if (_st == St.Idle)
            {
                UpdateIdleHover(p);
                return;
            }
            if (_st == St.Selecting && e.LeftButton == MouseButtonState.Pressed)
            {
                var px = ToPx(p);
                if (_spaceHeld)
                {
                    var delta = px - _lastPx;
                    _startPx += delta;
                }
                _lastPx = px;
                _curPx = px;
                var mods = Keyboard.Modifiers;
                Point a = _startPx, b = _curPx;
                if ((mods & ModifierKeys.Shift) != 0) b = ShapeAnnotation.Square(a, b);
                if ((mods & ModifierKeys.Alt) != 0) a = new Point(2 * _startPx.X - b.X, 2 * _startPx.Y - b.Y);
                _sel = ClampToMonitor(new Rect(a, b));
                _hoverRect.Visibility = Visibility.Collapsed;
                _hoverLabel.Visibility = Visibility.Collapsed;
                UpdateSelectionVisuals();
                if (Settings.Current.ShowMagnifier) ShowLoupe(p);
                return;
            }
            if (_st != St.Editing) return;

            switch (_edrag)
            {
                case EDrag.Surface:
                    _surface.OnMove(p, Keyboard.Modifiers);
                    break;
                case EDrag.MoveSel:
                {
                    var d = ToPx(p) - _startPx;
                    double x = Math.Clamp(_selAtStart.X + d.X, 0, MonW - _selAtStart.Width);
                    double y = Math.Clamp(_selAtStart.Y + d.Y, 0, MonH - _selAtStart.Height);
                    _sel = new Rect(x, y, _selAtStart.Width, _selAtStart.Height);
                    UpdateSelectionVisuals();
                    break;
                }
                case EDrag.Resize:
                {
                    var px = ToPx(p);
                    double l = _selAtStart.Left, t = _selAtStart.Top, r = _selAtStart.Right, b = _selAtStart.Bottom;
                    switch (_resizeHandle)
                    {
                        case 0: l = px.X; t = px.Y; break;
                        case 1: t = px.Y; break;
                        case 2: r = px.X; t = px.Y; break;
                        case 3: r = px.X; break;
                        case 4: r = px.X; b = px.Y; break;
                        case 5: b = px.Y; break;
                        case 6: l = px.X; b = px.Y; break;
                        case 7: l = px.X; break;
                    }
                    _sel = ClampToMonitor(new Rect(new Point(l, t), new Point(r, b)));
                    if (_sel.Width < 1) _sel.Width = 1;
                    if (_sel.Height < 1) _sel.Height = 1;
                    UpdateSelectionVisuals();
                    if (Settings.Current.ShowMagnifier) ShowLoupe(p);
                    break;
                }
                default:
                    if (!OverToolbar(e.OriginalSource)) _root.Cursor = EditCursorAt(p);
                    break;
            }
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(_root);
            _root.ReleaseMouseCapture();
            if (_st == St.Selecting)
            {
                _loupe.Visibility = Visibility.Collapsed;
                if (_sel.Width < 4 || _sel.Height < 4)
                {
                    // plain click: take the window under the cursor, or the whole screen
                    var px = ToPx(p);
                    var w = _session.WindowAt(Monitor.Bounds.X + (int)px.X, Monitor.Bounds.Y + (int)px.Y);
                    _sel = w != null
                        ? ClampToMonitor(new Rect(w.Bounds.X - Monitor.Bounds.X, w.Bounds.Y - Monitor.Bounds.Y, w.Bounds.Width, w.Bounds.Height))
                        : new Rect(0, 0, MonW, MonH);
                }
                _hoverRect.Visibility = Visibility.Collapsed;
                _hoverLabel.Visibility = Visibility.Collapsed;
                _st = St.Editing;
                UpdateSelectionVisuals();
                UpdateCursor();
                return;
            }
            if (_st == St.Editing)
            {
                if (_edrag == EDrag.Surface) _surface.OnUp(p);
                if (_edrag == EDrag.Resize) _loupe.Visibility = Visibility.Collapsed;
                _edrag = EDrag.None;
                UpdateCursor();
            }
        }

        // ------------------------------------------------------------------ keyboard

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (e.Key == Key.System) e.Handled = true;
            if (_surface.IsEditingText) return;
            var mods = Keyboard.Modifiers;
            bool ctrl = (mods & ModifierKeys.Control) != 0;

            if (key == Key.Escape)
            {
                e.Handled = true;
                _session.Cancel();
                return;
            }

            if (_st == St.Idle)
            {
                if (key == Key.Space && !e.IsRepeat)
                {
                    e.Handled = true;
                    _session.SetWindowMode(!_session.WindowMode);
                }
                return;
            }

            if (_st == St.Selecting)
            {
                if (key == Key.Space) { _spaceHeld = true; e.Handled = true; }
                return;
            }

            // editing
            e.Handled = true;
            if (key == Key.Enter || (ctrl && key == Key.C)) Finish(FinishAction.Copy);
            else if (ctrl && key == Key.S) Finish(FinishAction.Save);
            else if (ctrl && key == Key.P) Finish(FinishAction.Pin);
            else if (ctrl && (key == Key.Y || (key == Key.Z && (mods & ModifierKeys.Shift) != 0))) _surface.Redo();
            else if (ctrl && key == Key.Z) _surface.Undo();
            else if (key == Key.Delete || key == Key.Back) _surface.DeleteSelected();
            else if (key is Key.Left or Key.Right or Key.Up or Key.Down)
            {
                double step = (mods & ModifierKeys.Shift) != 0 ? 10 : 1;
                var v = key switch { Key.Left => new Vector(-step, 0), Key.Right => new Vector(step, 0), Key.Up => new Vector(0, -step), _ => new Vector(0, step) };
                var r = _sel; r.Offset(v);
                if (r.X >= 0 && r.Y >= 0 && r.Right <= MonW && r.Bottom <= MonH) { _sel = r; UpdateSelectionVisuals(); }
            }
            else if (!ctrl && _toolbar.HandleKey(key)) { }
            else e.Handled = false;
        }

        void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space) _spaceHeld = false;
            if (e.Key == Key.System) e.Handled = true;
        }

        // ------------------------------------------------------------------ finish

        void Finish(FinishAction action)
        {
            _surface.EndTextEdit();
            var crop = _img.Clamp(SelInt());
            var baseImg = _img.Crop(crop);
            var final = baseImg;
            if (_surface.Items.Any(a => !a.IsEmpty))
            {
                var overlay = AnnotationSurface.RenderOverlay(_surface.Items, _surface.Env, crop, _s);
                final = baseImg.CompositeOver(overlay);
            }
            var result = new CaptureResult
            {
                Image = final,
                Base = baseImg,
                Annotations = _surface.CloneItems(new Vector(-crop.X / _s, -crop.Y / _s)),
                Monitor = Monitor,
                Action = action,
                ScreenRect = new Int32Rect(Monitor.Bounds.X + crop.X, Monitor.Bounds.Y + crop.Y, crop.Width, crop.Height),
            };
            if (action == FinishAction.Save)
            {
                Topmost = false;
                var path = Output.SaveAs(final, this);
                Topmost = true;
                if (path == null) { Activate(); return; }
                result.SavedPath = path;
            }
            _session.Complete(result);
        }

        // ------------------------------------------------------------------ promo rendering (README images)

        System.Windows.Media.Imaging.BitmapSource RenderOffscreen()
        {
            _dim.BeginAnimation(OpacityProperty, null); _dim.Opacity = 1;
            foreach (var el in new FrameworkElement[] { _toolbar.Main, _toolbar.Options, _hint })
            {
                el.BeginAnimation(OpacityProperty, null);
                el.Opacity = 1;
                el.RenderTransform = null;
            }
            var size = new Size(MonW / _s, MonH / _s);
            for (int i = 0; i < 2; i++)
            {
                _root.Measure(size);
                _root.Arrange(new Rect(size));
                _root.UpdateLayout();
                if (_st == St.Editing) PlaceToolbar();
            }
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)MonW, (int)MonH, 96 * _s, 96 * _s, PixelFormats.Pbgra32);
            rtb.Render(_root);
            rtb.Freeze();
            return rtb;
        }

        public System.Windows.Media.Imaging.BitmapSource PromoEditing(Rect selPx, System.Collections.Generic.IEnumerable<Annotation> items, Tool tool, int selectIndex = -1)
        {
            _st = St.Editing;
            _sel = selPx;
            _surface.Load(items);
            _surface.Tool = tool;
            if (selectIndex >= 0) _surface.Select(_surface.Items[selectIndex]);
            UpdateSelectionVisuals();
            return RenderOffscreen();
        }

        public System.Windows.Media.Imaging.BitmapSource PromoSelecting(Rect selPx, Point cursorDip)
        {
            _st = St.Selecting;
            _sel = selPx;
            UpdateSelectionVisuals();
            ShowLoupe(cursorDip);
            return RenderOffscreen();
        }

        public System.Windows.Media.Imaging.BitmapSource PromoWindowHover(WindowInfo w)
        {
            _session.SetWindowMode(true);
            ShowWindowHover(w, true);
            ShowHint();
            return RenderOffscreen();
        }
    }
}
