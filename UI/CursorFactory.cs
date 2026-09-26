using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kadr.Core;
using Microsoft.Win32.SafeHandles;

namespace Kadr.UI
{
    /// <summary>Hardware cursors drawn at runtime (crisp at any DPI, no lag).</summary>
    public static class CursorFactory
    {
        static readonly Dictionary<string, Cursor> Cache = new();

        sealed class SafeIcon : SafeHandleZeroOrMinusOneIsInvalid
        {
            public SafeIcon(IntPtr h) : base(true) { SetHandle(h); }
            protected override bool ReleaseHandle() => Native.DestroyIcon(handle);
        }

        public static Cursor Crosshair(double scale)
        {
            string key = "cross" + scale;
            if (Cache.TryGetValue(key, out var c)) return c;
            int size = (int)Math.Round(25 * scale) | 1;
            double s = size;
            double mid = Math.Floor(s / 2) + 0.5;
            double gap = 3 * scale;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var outline = new Pen(new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), 3 * Math.Max(1, Math.Round(scale)));
                var line = new Pen(new SolidColorBrush(Color.FromArgb(255, 20, 20, 20)), Math.Max(1, Math.Round(scale)));
                foreach (var pen in new[] { outline, line })
                {
                    dc.DrawLine(pen, new Point(mid, 1.5), new Point(mid, mid - gap));
                    dc.DrawLine(pen, new Point(mid, mid + gap), new Point(mid, s - 1.5));
                    dc.DrawLine(pen, new Point(1.5, mid), new Point(mid - gap, mid));
                    dc.DrawLine(pen, new Point(mid + gap, mid), new Point(s - 1.5, mid));
                }
            }
            c = Build(dv, size, size, (int)mid, (int)mid);
            Cache[key] = c;
            return c;
        }

        public static Cursor Camera(double scale)
        {
            string key = "cam" + scale;
            if (Cache.TryGetValue(key, out var c)) return c;
            int size = (int)Math.Round(30 * scale);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(scale, scale));
                var body = Geometry.Parse("M4 9 A2 2 0 0 1 6 7 H9.5 L11.5 4.5 H18.5 L20.5 7 H24 A2 2 0 0 1 26 9 V22 A2 2 0 0 1 24 24 H6 A2 2 0 0 1 4 22 Z");
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), 3) { LineJoin = PenLineJoin.Round }, body);
                dc.DrawGeometry(Brushes.White, null, body);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(40, 40, 44)), null, new Point(15, 15.2), 5.2, 5.2);
                dc.DrawEllipse(Brushes.White, null, new Point(15, 15.2), 3.2, 3.2);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(40, 40, 44)), null, new Point(15, 15.2), 2.2, 2.2);
                dc.Pop();
            }
            c = Build(dv, size, size, size / 2, size / 2);
            Cache[key] = c;
            return c;
        }

        static Cursor Build(Visual v, int w, int h, int hx, int hy)
        {
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(v);
            var px = new byte[w * h * 4];
            rtb.CopyPixels(px, w * 4, 0);

            var bmi = new Native.BITMAPINFOHEADER { biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            IntPtr hdc = Native.GetDC(IntPtr.Zero);
            IntPtr color = Native.CreateDIBSection(hdc, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
            Native.ReleaseDC(IntPtr.Zero, hdc);
            Marshal.Copy(px, 0, bits, px.Length);
            IntPtr mask = Native.CreateBitmap(w, h, 1, 1, IntPtr.Zero);
            var info = new Native.ICONINFO { fIcon = false, xHotspot = hx, yHotspot = hy, hbmColor = color, hbmMask = mask };
            IntPtr icon = Native.CreateIconIndirect(ref info);
            Native.DeleteObject(color);
            Native.DeleteObject(mask);
            if (icon == IntPtr.Zero) return Cursors.Cross;
            return CursorInteropHelper.Create(new SafeIcon(icon));
        }
    }
}
