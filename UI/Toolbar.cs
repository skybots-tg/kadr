using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Kadr.Editor;

namespace Kadr.UI
{
    static class Theme
    {
        public static readonly Color Panel = Color.FromRgb(30, 30, 34);
        public static readonly Color AccentHover = Color.FromRgb(51, 149, 255);
        public static SolidColorBrush Brush(byte a, byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromArgb(a, r, g, b)); x.Freeze(); return x; }
        public static readonly Brush Hairline = Brush(26, 255, 255, 255);
        public static readonly Brush Divider = Brush(30, 255, 255, 255);
        public static readonly Brush Hover = Brush(24, 255, 255, 255);
        public static readonly Brush Pressed = Brush(40, 255, 255, 255);
        public static readonly Brush SegmentOn = Brush(46, 255, 255, 255);
        public static readonly Brush Well = Brush(16, 255, 255, 255);
        public static readonly Brush Muted = Brush(150, 255, 255, 255);
    }

    /// <summary>Rounded, tinted floating panel with a hairline border and an optional blurred backdrop.</summary>
    public class GlassPanel : Grid
    {
        const double BlurMargin = 40;
        readonly Rectangle _glass;
        readonly ImageBrush _brush;
        readonly Border _tint;
        public readonly Border Content;

        public GlassPanel(double radius = 12)
        {
            _brush = new ImageBrush { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
            _glass = new Rectangle
            {
                Fill = _brush,
                Margin = new Thickness(-BlurMargin),
                Effect = new BlurEffect { Radius = 40, RenderingBias = RenderingBias.Performance },
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
            };
            var clipHost = new Grid();
            clipHost.Children.Add(_glass);
            _tint = new Border { Background = new SolidColorBrush(Color.FromArgb(242, Theme.Panel.R, Theme.Panel.G, Theme.Panel.B)), CornerRadius = new CornerRadius(radius) };
            var inner = new Border { CornerRadius = new CornerRadius(radius), BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1), IsHitTestVisible = false };
            var outer = new Border { CornerRadius = new CornerRadius(radius + 1), BorderBrush = Theme.Brush(90, 0, 0, 0), BorderThickness = new Thickness(1), Margin = new Thickness(-1), IsHitTestVisible = false };
            Content = new Border { Padding = new Thickness(5) };
            Children.Add(clipHost);
            Children.Add(_tint);
            Children.Add(Content);
            Children.Add(inner);
            Children.Add(outer);
            SizeChanged += (_, _) => clipHost.Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), radius, radius);
            Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 8, Direction = 270, Opacity = 0.32, Color = Colors.Black };
            UseLayoutRounding = true;
        }

        /// <summary>Show a blurred copy of <paramref name="img"/> behind the panel; rectPx = panel rect in image pixels.</summary>
        public void SetBackdrop(ImageSource img, Rect rectPx, double scale)
        {
            if (img == null) { _glass.Visibility = Visibility.Collapsed; return; }
            _brush.ImageSource = img;
            double m = BlurMargin * scale;
            _brush.Viewbox = new Rect(rectPx.X - m, rectPx.Y - m, rectPx.Width + 2 * m, rectPx.Height + 2 * m);
            _glass.Visibility = Visibility.Visible;
            _tint.Background = new SolidColorBrush(Color.FromArgb(205, Theme.Panel.R, Theme.Panel.G, Theme.Panel.B));
        }
    }

    public sealed class ToolButton : Border
    {
        bool _active, _hover, _enabled = true, _primary;
        public event Action Click;

        public ToolButton(FrameworkElement content, string tip, double w = 32, double h = 32, bool primary = false)
        {
            _primary = primary;
            Width = w; Height = h;
            CornerRadius = new CornerRadius(8);
            Margin = new Thickness(1, 0, 1, 0);
            Child = content;
            content.HorizontalAlignment = HorizontalAlignment.Center;
            content.VerticalAlignment = VerticalAlignment.Center;
            ToolTip = tip;
            ToolTipService.SetInitialShowDelay(this, 500);
            ToolTipService.SetPlacement(this, System.Windows.Controls.Primitives.PlacementMode.Top);
            ToolTipService.SetVerticalOffset(this, -4);
            Cursor = Cursors.Arrow;
            MouseEnter += (_, _) => { _hover = true; Update(); };
            MouseLeave += (_, _) => { _hover = false; Update(); };
            MouseLeftButtonDown += (_, e) => { e.Handled = true; if (_enabled) { Background = _primary ? new SolidColorBrush(AnnotationSurface.Accent) : Theme.Pressed; CaptureMouse(); } };
            MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                bool inside = IsMouseOver;
                ReleaseMouseCapture();
                Update();
                if (_enabled && inside) Click?.Invoke();
            };
            Update();
        }

        public bool Active { get => _active; set { _active = value; Update(); } }
        public bool Enabled { get => _enabled; set { _enabled = value; Opacity = value ? 1 : 0.32; Update(); } }

        void Update()
        {
            if (_primary) Background = new SolidColorBrush(_hover ? Theme.AccentHover : AnnotationSurface.Accent);
            else Background = _active ? new SolidColorBrush(AnnotationSurface.Accent) : (_hover && _enabled ? Theme.Hover : Brushes.Transparent);
        }
    }

    /// <summary>Compact segmented control (iOS-like).</summary>
    public sealed class Segmented : Border
    {
        readonly List<Border> _items = new();
        int _selected = -1;

        public Segmented(IEnumerable<(FrameworkElement content, string tip)> items, Action<int> pick, double minWidth = 30)
        {
            Background = Theme.Well;
            CornerRadius = new CornerRadius(8);
            Padding = new Thickness(2);
            Margin = new Thickness(2, 0, 2, 0);
            VerticalAlignment = VerticalAlignment.Center;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            int i = 0;
            foreach (var (content, tip) in items)
            {
                content.HorizontalAlignment = HorizontalAlignment.Center;
                content.VerticalAlignment = VerticalAlignment.Center;
                var b = new Border
                {
                    Child = content, MinWidth = minWidth, Height = 26, CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 0, 8, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                };
                if (tip != null) b.ToolTip = tip;
                int idx = i++;
                b.MouseEnter += (_, _) => { if (idx != _selected) b.Background = Theme.Hover; };
                b.MouseLeave += (_, _) => { if (idx != _selected) b.Background = Brushes.Transparent; };
                b.MouseLeftButtonDown += (_, e) => { e.Handled = true; pick(idx); };
                _items.Add(b);
                row.Children.Add(b);
            }
            Child = row;
        }

        public int Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                for (int i = 0; i < _items.Count; i++) _items[i].Background = i == value ? Theme.SegmentOn : Brushes.Transparent;
            }
        }
    }

    /// <summary>Main tool bar plus a contextual options strip for the active tool.</summary>
    public sealed class Toolbar
    {
        readonly AnnotationSurface _s;
        readonly Dictionary<Tool, ToolButton> _toolButtons = new();
        readonly ToolButton _undo, _redo;
        public readonly GlassPanel Main;
        public readonly GlassPanel Options;
        readonly StackPanel _optionsRow = new() { Orientation = Orientation.Horizontal };
        string _optionsKind;
        readonly List<(Color c, Ellipse ring)> _swatches = new();
        Segmented _sizeSeg, _textSeg, _blurSeg;

        public event Action CopyClicked, SaveClicked, PinClicked, CloseClicked;
        /// <summary>Raised when the options strip appears, disappears or changes size.</summary>
        public event Action OptionsChanged;

        public static readonly (Tool tool, string icon, string tip)[] ToolDefs =
        {
            (Tool.Arrow, Icons.Arrow, "Стрелка — A"),
            (Tool.Rect, Icons.Rect, "Прямоугольник — R"),
            (Tool.Ellipse, Icons.Ellipse, "Овал — O"),
            (Tool.Pen, Icons.Pen, "Карандаш — P"),
            (Tool.Marker, Icons.Marker, "Маркер — H"),
            (Tool.Text, Icons.Text, "Текст — T"),
            (Tool.Counter, null, "Нумерация — N"),
            (Tool.Blur, Icons.Blur, "Размытие — B"),
        };

        public Toolbar(AnnotationSurface surface, bool showPin = true)
        {
            _s = surface;

            Main = new GlassPanel();
            var bar = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (tool, icon, tip) in ToolDefs)
            {
                var btn = new ToolButton(icon == null ? Icons.Counter() : Icons.Make(icon), tip);
                var t = tool;
                btn.Click += () => _s.Tool = _s.Tool == t ? Tool.None : t;
                _toolButtons[tool] = btn;
                bar.Children.Add(btn);
            }
            bar.Children.Add(Sep());
            _undo = new ToolButton(Icons.Make(Icons.Undo), "Отменить — Ctrl+Z");
            _undo.Click += () => _s.Undo();
            _redo = new ToolButton(Icons.Make(Icons.Redo), "Повторить — Ctrl+Y");
            _redo.Click += () => _s.Redo();
            bar.Children.Add(_undo);
            bar.Children.Add(_redo);
            bar.Children.Add(Sep());
            if (showPin)
            {
                var pin = new ToolButton(Icons.Make(Icons.Pin), "Закрепить поверх окон — Ctrl+P");
                pin.Click += () => PinClicked?.Invoke();
                bar.Children.Add(pin);
            }
            var save = new ToolButton(Icons.Make(Icons.Save), "Сохранить как… — Ctrl+S");
            save.Click += () => SaveClicked?.Invoke();
            bar.Children.Add(save);

            var copyContent = new StackPanel { Orientation = Orientation.Horizontal };
            copyContent.Children.Add(Icons.Make(Icons.Copy, 15, Brushes.White, 1.9));
            copyContent.Children.Add(new TextBlock { Text = "Копировать", Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(7, 0, 0, 1), VerticalAlignment = VerticalAlignment.Center, FontFamily = KFonts.Family });
            var copy = new ToolButton(copyContent, "Скопировать и закрыть — Enter", 122, 32, primary: true) { Margin = new Thickness(6, 0, 2, 0) };
            copy.Click += () => CopyClicked?.Invoke();
            bar.Children.Add(copy);
            var close = new ToolButton(Icons.Make(Icons.Close, 16), "Отмена — Esc");
            close.Click += () => CloseClicked?.Invoke();
            bar.Children.Add(close);
            Main.Content.Child = bar;

            Options = new GlassPanel { Visibility = Visibility.Collapsed };
            Options.Content.Padding = new Thickness(6, 4, 6, 4);
            Options.Content.Child = _optionsRow;
            Options.SizeChanged += (_, _) => OptionsChanged?.Invoke();

            _s.StateChanged += UpdateState;
            UpdateState();
        }

        static FrameworkElement Sep() => new Rectangle { Width = 1, Height = 18, Fill = Theme.Divider, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };

        // ------------------------------------------------------------------ options strip

        string ContextKind()
        {
            var sel = _s.Selected;
            if (sel is BlurAnnotation || (sel == null && _s.Tool == Tool.Blur)) return "blur";
            if (sel is TextAnnotation || (sel == null && _s.Tool == Tool.Text)) return "text";
            if (sel is CounterAnnotation || (sel == null && _s.Tool == Tool.Counter)) return "counter";
            if (sel != null || _s.Tool != Tool.None) return "stroke";
            return null;
        }

        void BuildOptions(string kind)
        {
            _optionsRow.Children.Clear();
            _swatches.Clear();
            _textSeg = _blurSeg = null;

            if (kind == "blur")
            {
                _blurSeg = new Segmented(new (FrameworkElement, string)[]
                {
                    (Label("Пиксели"), "Мозаика"),
                    (Label("Размытие"), "Мягкое размытие"),
                    (Label("Закрасить"), "Сплошная плашка — надёжно скрывает"),
                    (Label("Фокус"), "Размыть всё вокруг области"),
                }, i => _s.BlurKind = (BlurKind)i);
                _optionsRow.Children.Add(_blurSeg);
                _optionsRow.Children.Add(Sep());
                _sizeSeg = new Segmented(new (FrameworkElement, string)[]
                {
                    (Dot(5), "Слабее"), (Dot(8), "Средне"), (Dot(11), "Сильнее"),
                }, i => _s.SizeLevel = i, 28);
                _optionsRow.Children.Add(_sizeSeg);
                return;
            }

            var swatchRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 2, 0) };
            for (int n = 0; n < AnnotationSurface.Palette.Length; n++)
            {
                var c = AnnotationSurface.Palette[n];
                var dot = new Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(c), Stroke = Theme.Brush(70, 255, 255, 255), StrokeThickness = c.R + c.G + c.B < 150 ? 1 : 0 };
                var ring = new Ellipse { Width = 24, Height = 24, StrokeThickness = 2, Stroke = Brushes.Transparent };
                var cell = new Grid { Width = 26, Height = 30, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = $"Цвет — {n + 1}" };
                cell.Children.Add(ring);
                cell.Children.Add(dot);
                var cc = c;
                cell.MouseLeftButtonDown += (_, e) => { e.Handled = true; _s.Color = cc; };
                cell.MouseEnter += (_, _) => dot.RenderTransform = new ScaleTransform(1.15, 1.15, 8, 8);
                cell.MouseLeave += (_, _) => dot.RenderTransform = null;
                _swatches.Add((c, ring));
                swatchRow.Children.Add(cell);
            }
            _optionsRow.Children.Add(swatchRow);
            _optionsRow.Children.Add(Sep());

            (FrameworkElement, string)[] sizes = kind switch
            {
                "text" => new (FrameworkElement, string)[] { (Glyph("A", 11), "Мелкий"), (Glyph("A", 14), "Средний"), (Glyph("A", 17), "Крупный") },
                "counter" => new (FrameworkElement, string)[] { (Dot(7), "Мелкие"), (Dot(10), "Средние"), (Dot(13), "Крупные") },
                _ => new (FrameworkElement, string)[] { (Line(1.5), "Тонкая"), (Line(3), "Средняя"), (Line(5), "Толстая") },
            };
            _sizeSeg = new Segmented(sizes, i => _s.SizeLevel = i, 30);
            _optionsRow.Children.Add(_sizeSeg);

            if (kind == "text")
            {
                _optionsRow.Children.Add(Sep());
                _textSeg = new Segmented(new (FrameworkElement, string)[]
                {
                    (Label("Обводка"), "Цветной текст с белым контуром"),
                    (Label("Плашка"), "Белый текст на цветной подложке"),
                    (Label("Простой"), "Только цвет, с лёгкой тенью"),
                }, i => _s.TextStyle = (TextStyleKind)i);
                _optionsRow.Children.Add(_textSeg);
            }
        }

        static FrameworkElement Label(string t) => new TextBlock { Text = t, Foreground = Brushes.White, FontSize = 12.5, FontFamily = KFonts.Family, FontWeight = FontWeights.Medium, Margin = new Thickness(2, 0, 2, 1) };
        static FrameworkElement Dot(double d) => new Ellipse { Width = d, Height = d, Fill = Icons.Ink };
        static FrameworkElement Line(double t) => new Rectangle { Width = 16, Height = t, RadiusX = t / 2, RadiusY = t / 2, Fill = Icons.Ink };
        static FrameworkElement Glyph(string t, double size) => new TextBlock { Text = t, FontSize = size, FontWeight = FontWeights.SemiBold, Foreground = Icons.Ink, FontFamily = KFonts.Family };

        public bool OptionsVisible => Options.Visibility == Visibility.Visible;

        void SetOptions(string kind)
        {
            if (kind != _optionsKind)
            {
                bool wasHidden = _optionsKind == null;
                _optionsKind = kind;
                if (kind == null)
                {
                    Options.Visibility = Visibility.Collapsed;
                    OptionsChanged?.Invoke();
                    return;
                }
                BuildOptions(kind);
                Options.Visibility = Visibility.Visible;
                if (wasHidden)
                {
                    Options.Opacity = 0;
                    Options.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
                }
                OptionsChanged?.Invoke();
            }
            if (kind == null) return;
            foreach (var (c, ring) in _swatches) ring.Stroke = c == _s.Color ? Brushes.White : Brushes.Transparent;
            if (_sizeSeg != null) _sizeSeg.Selected = _s.SizeLevel;
            if (_textSeg != null) _textSeg.Selected = (int)_s.TextStyle;
            if (_blurSeg != null) _blurSeg.Selected = (int)_s.BlurKind;
        }

        public void HideOptions() => SetOptions(null);

        void UpdateState()
        {
            foreach (var kv in _toolButtons) kv.Value.Active = _s.Tool == kv.Key;
            _undo.Enabled = _s.CanUndo;
            _redo.Enabled = _s.CanRedo;
            SetOptions(ContextKind());
        }

        /// <summary>Single-key shortcuts: tools, 1–9 colors, [ ] size. Returns true if handled.</summary>
        public bool HandleKey(Key key)
        {
            if (key >= Key.D1 && key <= Key.D9 || key >= Key.NumPad1 && key <= Key.NumPad9)
            {
                int i = key >= Key.NumPad1 ? key - Key.NumPad1 : key - Key.D1;
                if (i < AnnotationSurface.Palette.Length) _s.Color = AnnotationSurface.Palette[i];
                return true;
            }
            if (key == Key.OemOpenBrackets) { _s.SizeLevel--; return true; }
            if (key == Key.OemCloseBrackets) { _s.SizeLevel++; return true; }
            Tool? t = key switch
            {
                Key.A => Tool.Arrow, Key.R => Tool.Rect, Key.O => Tool.Ellipse, Key.E => Tool.Ellipse, Key.P => Tool.Pen,
                Key.H => Tool.Marker, Key.M => Tool.Marker, Key.T => Tool.Text, Key.N => Tool.Counter, Key.B => Tool.Blur,
                Key.V => Tool.None,
                _ => null,
            };
            if (t == null) return false;
            _s.Tool = _s.Tool == t ? Tool.None : t.Value;
            return true;
        }
    }
}
