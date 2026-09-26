using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Kadr.Capture;
using Kadr.Core;
using Kadr.Editor;

namespace Kadr.UI
{
    /// <summary>Standalone markup window (opened from the preview thumbnail or for window shots).</summary>
    public sealed class EditorWindow : Window
    {
        readonly CaptureResult _r;
        readonly AnnotationSurface _surface = new();
        readonly Toolbar _toolbar;
        readonly Grid _stage;
        readonly ScaleTransform _zoom = new();
        readonly Border _paper;
        bool _dragging;

        public EditorWindow(CaptureResult r)
        {
            _r = r;
            Title = "Кадр — редактор";
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 27));
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var img = r.Base;
            double iw = img.DipWidth, ih = img.DipHeight;
            var wa = SystemParameters.WorkArea;
            Width = Math.Clamp(iw + 160, 980, Math.Max(980, wa.Width * 0.9));
            Height = Math.Clamp(ih + 220, 640, Math.Max(640, wa.Height * 0.9));
            MinWidth = 820; MinHeight = 520;
            Icon = App.AppIconImage;

            var picture = new Image { Source = img.ScaledBitmap(), Width = iw, Height = ih, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            _surface.Env = new RenderEnv { Base = img, BaseOrigin = new Point(0, 0) };
            _surface.Width = iw; _surface.Height = ih;
            _surface.Background = Brushes.Transparent; // receive mouse everywhere on the image
            _surface.Load(r.Annotations.Select(a => a.Clone()));

            var layers = new Grid { Width = iw, Height = ih, ClipToBounds = true };
            layers.Children.Add(picture);
            layers.Children.Add(_surface);

            _paper = new Border
            {
                Child = layers,
                LayoutTransform = _zoom,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = r.IsWindow ? null : new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.5 },
            };

            _stage = new Grid { Margin = new Thickness(40, 30, 40, 96), ClipToBounds = false };
            _stage.Children.Add(_paper);

            _toolbar = new Toolbar(_surface, showPin: true);
            _toolbar.CopyClicked += () => Done();
            _toolbar.SaveClicked += SaveAs;
            _toolbar.PinClicked += () => { App.PinImage(Render(), null); Close(); };
            _toolbar.CloseClicked += Close;
            var bottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 22) };
            _toolbar.Options.HorizontalAlignment = HorizontalAlignment.Center;
            _toolbar.Options.Margin = new Thickness(0, 0, 0, 6);
            bottom.Children.Add(_toolbar.Options);
            bottom.Children.Add(_toolbar.Main);

            var root = new Grid();
            root.Children.Add(_stage);
            root.Children.Add(bottom);
            Content = root;

            _stage.SizeChanged += (_, _) => FitZoom();
            _surface.MouseLeftButtonDown += OnDown;
            _surface.MouseMove += OnMove;
            _surface.MouseLeftButtonUp += OnUp;
            _surface.StateChanged += () => _surface.Cursor = _surface.Tool == Tool.None ? Cursors.Arrow : (_surface.Tool == Tool.Text ? Cursors.IBeam : Cursors.Cross);
            PreviewKeyDown += OnKey;
            if (_surface.Tool == Tool.None) _surface.Tool = Tool.Arrow;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            int dark = 1;
            Native.DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, 4);
            int caption = 0x001B1818; // COLORREF 0x00BBGGRR
            Native.DwmSetWindowAttribute(hwnd, 35 /* DWMWA_CAPTION_COLOR */, ref caption, 4);
        }

        void FitZoom()
        {
            double aw = Math.Max(50, _stage.ActualWidth), ah = Math.Max(50, _stage.ActualHeight);
            double z = Math.Min(1, Math.Min(aw / _r.Base.DipWidth, ah / _r.Base.DipHeight));
            _zoom.ScaleX = _zoom.ScaleY = z;
        }

        void OnDown(object s, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(_surface);
            if (_surface.OnDown(p, Keyboard.Modifiers, e.ClickCount))
            {
                if (!_surface.IsEditingText) { _dragging = true; _surface.CaptureMouse(); }
                e.Handled = true;
            }
        }

        void OnMove(object s, MouseEventArgs e)
        {
            var p = e.GetPosition(_surface);
            if (_dragging) { _surface.OnMove(p, Keyboard.Modifiers); return; }
            var c = _surface.CursorAt(p);
            _surface.Cursor = c ?? (_surface.Tool == Tool.None ? Cursors.Arrow : (_surface.Tool == Tool.Text ? Cursors.IBeam : Cursors.Cross));
        }

        void OnUp(object s, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            _surface.ReleaseMouseCapture();
            _surface.OnUp(e.GetPosition(_surface));
        }

        void OnKey(object s, KeyEventArgs e)
        {
            if (_surface.IsEditingText) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;
            bool ctrl = (mods & ModifierKeys.Control) != 0;
            e.Handled = true;
            if (key == Key.Escape) Close();
            else if (key == Key.Enter || (ctrl && key == Key.C)) Done();
            else if (ctrl && key == Key.S) SaveAs();
            else if (ctrl && (key == Key.Y || (key == Key.Z && (mods & ModifierKeys.Shift) != 0))) _surface.Redo();
            else if (ctrl && key == Key.Z) _surface.Undo();
            else if (key == Key.Delete || key == Key.Back) _surface.DeleteSelected();
            else if (!ctrl && _toolbar.HandleKey(key)) { }
            else e.Handled = false;
        }

        PixelImage Render()
        {
            _surface.EndTextEdit();
            var b = _r.Base;
            if (!_surface.Items.Any(a => !a.IsEmpty)) return b;
            var overlay = AnnotationSurface.RenderOverlay(_surface.Items, _surface.Env, new Int32Rect(0, 0, b.Width, b.Height), b.Scale);
            return b.CompositeOver(overlay);
        }

        void Done()
        {
            var img = Render();
            Output.CopyToClipboard(img);
            // keep the auto-saved file in sync with the edits
            if (_r.SavedPath != null && File.Exists(_r.SavedPath))
            {
                try { File.WriteAllBytes(_r.SavedPath, Output.EncodePng(img)); } catch { }
            }
            _r.Image = img;
            _r.Annotations = _surface.CloneItems(new Vector());
            Close();
        }

        void SaveAs()
        {
            var path = Output.SaveAs(Render(), this);
            if (path != null) Close();
        }
    }
}
