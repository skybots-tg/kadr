using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Kadr.Core;

namespace Kadr.Capture
{
    /// <summary>Turns a raw window image into a macOS-style shot: rounded corners + soft drop shadow on transparency.</summary>
    public static class WindowStyler
    {
        // All in DIPs, multiplied by the monitor scale.
        const double Radius = 10;
        const double SideMargin = 42, TopMargin = 30, BottomMargin = 60;

        public static PixelImage Style(PixelImage window, double scale, bool shadow)
        {
            double s = scale;
            double w = window.Width / s, h = window.Height / s;
            double ml = shadow ? SideMargin : 0, mt = shadow ? TopMargin : 0, mb = shadow ? BottomMargin : 0;
            int pw = (int)Math.Round(window.Width + 2 * ml * s);
            int ph = (int)Math.Round(window.Height + (mt + mb) * s);
            double ox = Math.Round(ml * s) / s, oy = Math.Round(mt * s) / s;

            var bmp = BitmapSource.Create(window.Width, window.Height, 96 * s, 96 * s, PixelFormats.Pbgra32, null, window.Pixels, window.Stride);
            bmp.Freeze();

            var rect = new Rect(ox, oy, w, h);
            var clip = new RectangleGeometry(rect, Radius, Radius);

            var content = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(content, BitmapScalingMode.NearestNeighbor);
            using (var dc = content.RenderOpen())
            {
                dc.PushClip(clip);
                dc.DrawImage(bmp, rect);
                dc.Pop();
                // hairline so light windows don't melt into light backgrounds
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), 1.0 / s);
                pen.Freeze();
                double inset = 0.5 / s;
                dc.DrawRoundedRectangle(null, pen, new Rect(rect.X + inset, rect.Y + inset, rect.Width - 2 * inset, rect.Height - 2 * inset), Radius, Radius);
            }

            Visual root = content;
            if (shadow)
            {
                var tight = new ContainerVisual
                {
                    Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Direction = 270, Opacity = 0.35, Color = Colors.Black, RenderingBias = RenderingBias.Quality }
                };
                tight.Children.Add(content);
                var wide = new ContainerVisual
                {
                    Effect = new DropShadowEffect { BlurRadius = 60, ShadowDepth = 20, Direction = 270, Opacity = 0.5, Color = Colors.Black, RenderingBias = RenderingBias.Quality }
                };
                wide.Children.Add(tight);
                root = wide;
            }

            var host = new ContainerVisual();
            host.Children.Add(root);
            var rtb = new RenderTargetBitmap(pw, ph, 96 * s, 96 * s, PixelFormats.Pbgra32);
            rtb.Render(host);
            var px = new byte[pw * ph * 4];
            rtb.CopyPixels(px, pw * 4, 0);
            return new PixelImage(pw, ph, px, s);
        }
    }
}
