using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kadr.Capture;
using Kadr.Editor;
using Kadr.UI;

namespace Kadr.Core
{
    /// <summary>
    /// Renders README scenes offscreen with the real UI (kadr --promo &lt;assets&gt; &lt;out&gt;).
    /// Assets are rendered demo pages, so no personal screen content ever ends up in the images.
    /// Coordinates below are CSS pixels of those pages (= DIPs at 125 %).
    /// </summary>
    public static class Promo
    {
        const double S = 1.25;

        public static void Run(string assets, string outDir)
        {
            Directory.CreateDirectory(outDir);
            Settings.Load();
            Settings.Current.Folder = @"C:\Users\User\Pictures\Screenshots";
            Settings.Current.Hotkeys = Hotkeys.Defaults();
            Settings.Current.LastColor = "#FFFF3B30";
            Settings.Current.LastSize = 1;
            Settings.Current.TextStyle = 0;
            Settings.Current.BlurKind = 0;

            var desk = Load(Path.Combine(assets, "desk.png"));
            var winScene = Load(Path.Combine(assets, "winscene.png"));
            var app = Load(Path.Combine(assets, "app.png"));
            var red = AnnotationSurface.Palette[0];

            // 1. hero: in-place markup over a dashboard
            var hero = new List<Annotation>();
            hero.Add(new ShapeAnnotation { A = new Point(282, 114), B2 = new Point(619, 264), Color = red, SizeLevel = 1 });
            hero.Add(new ArrowAnnotation { Start = new Point(560, 618), End = new Point(1024, 438), Control = new Point(760, 440), Color = red, SizeLevel = 1 });
            hero.Add(new TextAnnotation { Position = new Point(330, 626), Text = "Рост после рассылки", Color = red, SizeLevel = 1, Style = TextStyleKind.Outline });
            hero.Add(new CounterAnnotation { Center = new Point(584, 152), Number = 1, Color = red, SizeLevel = 1 });
            hero.Add(new CounterAnnotation { Center = new Point(925, 152), Number = 2, Color = red, SizeLevel = 1 });
            Save(Overlay(desk).PromoEditing(Px(264, 96, 1316, 716), hero, Tool.Arrow), outDir, "scene_hero.png");

            // 2. selecting with the loupe
            Save(Overlay(desk).PromoSelecting(Px(640, 300, 968, 484), new Point(968, 484)), outDir, "scene_select.png");

            // 3. window mode hover
            var w = new WindowInfo { Bounds = new Int32Rect(480, 220, 1600, 1000), Title = "Orbit — Задачи" };
            Save(Overlay(winScene).PromoWindowHover(w), outDir, "scene_window.png");

            // 4. the resulting macOS-style window shot
            var styled = WindowStyler.Style(new PixelImage(app.Width, app.Height, app.Pixels, 1), S, true);
            File.WriteAllBytes(Path.Combine(outDir, "window_shot.png"), Output.EncodePng(styled));

            // 5. blur kinds on the team card
            var crop = desk.Crop(new Int32Rect((int)(1332 * S), (int)(286 * S), (int)(456 * S), (int)(404 * S)));
            var env = new RenderEnv { Base = crop };
            var kinds = new[] { BlurKind.Pixelate, BlurKind.Gaussian, BlurKind.Redact, BlurKind.Focus };
            foreach (var k in kinds)
            {
                var items = new List<Annotation>();
                if (k == BlurKind.Focus) items.Add(new BlurAnnotation { A = new Point(12, 150), B2 = new Point(444, 214), Kind = k, SizeLevel = 1 });
                else
                {
                    for (int row = 0; row < 3; row++)
                        items.Add(new BlurAnnotation { A = new Point(80, 114 + row * 76), B2 = new Point(290, 134 + row * 76), Kind = k, SizeLevel = 1 });
                }
                var ov = AnnotationSurface.RenderOverlay(items, env, new Int32Rect(0, 0, crop.Width, crop.Height), S);
                File.WriteAllBytes(Path.Combine(outDir, $"blur_{k.ToString().ToLowerInvariant()}.png"), Output.EncodePng(crop.CompositeOver(ov)));
            }

            // 6. settings and installer cards
            var sw = SettingsWindow.CreateForPromo();
            var sroot = sw.PromoRoot;
            ((ScrollViewer)sw.Content).Content = null;
            SaveElement(sroot, 600, outDir, "settings.png");

            var iw = InstallWindow.ForInstall(promo: true);
            var iroot = (FrameworkElement)iw.Content;
            iw.Content = null;
            SaveElement(iroot, 460, outDir, "install.png");
        }

        static Rect Px(double x1, double y1, double x2, double y2) => new(Math.Round(x1 * S), Math.Round(y1 * S), Math.Round((x2 - x1) * S), Math.Round((y2 - y1) * S));

        static OverlayWindow Overlay(PixelImage img)
        {
            var mon = new MonitorInfo { Bounds = new Int32Rect(0, 0, img.Width, img.Height), WorkArea = new Int32Rect(0, 0, img.Width, img.Height), Scale = S, Primary = true };
            var snap = new ScreenSnapshot { Image = img, VirtualBounds = mon.Bounds, Monitors = new List<MonitorInfo> { mon }, Windows = new List<WindowInfo>() };
            var session = new CaptureSession(snap, _ => { });
            return new OverlayWindow(session, mon, new PixelImage(img.Width, img.Height, img.Pixels, S));
        }

        static PixelImage Load(string path)
        {
            var dec = BitmapDecoder.Create(new Uri(Path.GetFullPath(path)), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return PixelImage.FromBitmap(dec.Frames[0], S);
        }

        static void Save(BitmapSource bmp, string dir, string name)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(Path.Combine(dir, name));
            enc.Save(fs);
        }

        static void SaveElement(FrameworkElement content, double width, string dir, string name)
        {
            var host = new Border { Background = new SolidColorBrush(DarkWindow.Bg), Child = content, Width = width };
            TextElement.SetFontFamily(host, new FontFamily("Segoe UI Variable Text, Segoe UI"));
            TextElement.SetForeground(host, DarkWindow.TextMain);
            host.Measure(new Size(width, double.PositiveInfinity));
            host.Arrange(new Rect(host.DesiredSize));
            host.UpdateLayout();
            const double k = 2;
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth * k), (int)Math.Ceiling(host.ActualHeight * k), 96 * k, 96 * k, PixelFormats.Pbgra32);
            rtb.Render(host);
            Save(rtb, dir, name);
        }
    }
}
