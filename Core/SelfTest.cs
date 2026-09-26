using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Kadr.Capture;
using Kadr.Editor;

namespace Kadr.Core
{
    /// <summary>Renders sample annotations and a styled window shot to PNGs (kadr --selftest &lt;dir&gt;).</summary>
    public static class SelfTest
    {
        public static void Run(string dir)
        {
            Directory.CreateDirectory(dir);
            double s = 1.25;
            int w = 900, h = 560;
            // synthetic "screen": light UI with some text-like bars
            var px = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    byte v = (byte)(236 + ((x / 40 + y / 40) % 2) * 8);
                    bool bar = (y % 46) is > 18 and < 30 && x > 60 && x < 60 + (y * 7 % 600);
                    px[i] = px[i + 1] = px[i + 2] = bar ? (byte)70 : v;
                    if (x > 600 && y > 300) { px[i] = 200; px[i + 1] = 120; px[i + 2] = 40; }
                    px[i + 3] = 255;
                }
            var img = new PixelImage(w, h, px, s);
            var env = new RenderEnv { Base = img };
            var red = AnnotationSurface.Palette[0];
            var items = new List<Annotation>
            {
                new ArrowAnnotation { Start = new Point(40, 60), End = new Point(260, 150), Color = red, SizeLevel = 0 },
                new ArrowAnnotation { Start = new Point(40, 110), End = new Point(260, 200), Color = red, SizeLevel = 1 },
                new ArrowAnnotation { Start = new Point(40, 160), End = new Point(260, 250), Color = red, SizeLevel = 2 },
                new ArrowAnnotation { Start = new Point(300, 260), End = new Point(520, 250), Control = new Point(420, 120), Color = AnnotationSurface.Palette[4], SizeLevel = 1 },
                new ShapeAnnotation { A = new Point(320, 40), B2 = new Point(480, 110), Color = red, SizeLevel = 1 },
                new ShapeAnnotation { A = new Point(500, 40), B2 = new Point(640, 120), Color = AnnotationSurface.Palette[3], SizeLevel = 1, IsEllipse = true },
                new TextAnnotation { Position = new Point(40, 290), Text = "Обводка: нажми сюда", Color = red, SizeLevel = 1, Style = TextStyleKind.Outline },
                new TextAnnotation { Position = new Point(40, 335), Text = "Плашка", Color = AnnotationSurface.Palette[4], SizeLevel = 1, Style = TextStyleKind.Pill },
                new TextAnnotation { Position = new Point(40, 380), Text = "Простой текст\nв две строки", Color = AnnotationSurface.Palette[8], SizeLevel = 1, Style = TextStyleKind.Plain },
                new CounterAnnotation { Center = new Point(300, 320), Number = 1, Color = red, SizeLevel = 1 },
                new CounterAnnotation { Center = new Point(340, 320), Number = 2, Color = AnnotationSurface.Palette[2], SizeLevel = 1 },
                new BlurAnnotation { A = new Point(60, 14), B2 = new Point(200, 38), Kind = BlurKind.Pixelate, SizeLevel = 1 },
                new BlurAnnotation { A = new Point(210, 14), B2 = new Point(350, 38), Kind = BlurKind.Gaussian, SizeLevel = 1 },
                new BlurAnnotation { A = new Point(360, 14), B2 = new Point(500, 38), Kind = BlurKind.Redact, SizeLevel = 1 },
                new BlurAnnotation { A = new Point(560, 160), B2 = new Point(700, 230), Kind = BlurKind.Focus, SizeLevel = 1 },
            };
            var pen = new PenAnnotation { Color = red, SizeLevel = 1 };
            for (int i = 0; i < 60; i++) pen.AddPoint(new Point(420 + i * 3, 330 + Math.Sin(i / 6.0) * 30));
            items.Add(pen);
            var marker = new PenAnnotation { Color = AnnotationSurface.Palette[2], SizeLevel = 1, Marker = true };
            marker.Points.Add(new Point(60, 440)); marker.Points.Add(new Point(330, 440));
            items.Add(marker);

            var overlay = AnnotationSurface.RenderOverlay(items, env, new Int32Rect(0, 0, w, h), s);
            File.WriteAllBytes(Path.Combine(dir, "annotations.png"), Output.EncodePng(img.CompositeOver(overlay)));

            // styled window shot
            var win = img.Crop(new Int32Rect(0, 0, 640, 400));
            var styled = WindowStyler.Style(new PixelImage(win.Width, win.Height, win.Pixels, 1), s, true);
            File.WriteAllBytes(Path.Combine(dir, "window.png"), Output.EncodePng(styled));

            // the same on a colored background to judge the shadow
            var bgPx = new byte[styled.Width * styled.Height * 4];
            for (int i = 0; i < bgPx.Length; i += 4) { bgPx[i] = 230; bgPx[i + 1] = 200; bgPx[i + 2] = 170; bgPx[i + 3] = 255; }
            var onBg = new PixelImage(styled.Width, styled.Height, bgPx, s).CompositeOver(styled.Pixels);
            File.WriteAllBytes(Path.Combine(dir, "window_on_bg.png"), Output.EncodePng(onBg));
        }
    }
}
