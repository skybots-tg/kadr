using System;
using System.Threading.Tasks;
using Kadr.Core;

namespace Kadr.Capture
{
    /// <summary>
    /// Turns a raw window image into a macOS-style shot: smooth rounded corners, a hairline rim that runs
    /// the whole perimeter, and a layered soft shadow on transparency. Everything is computed per pixel,
    /// so edges are exact and anti-aliased regardless of DPI.
    /// </summary>
    public static class WindowStyler
    {
        // DIPs, multiplied by the monitor scale
        const double Radius = 10;
        const double SideMargin = 44, TopMargin = 32, BottomMargin = 64;

        // (blur sigma, vertical offset, opacity) — contact, mid and ambient layers
        static readonly (double sigma, double dy, double opacity)[] Shadows =
        {
            (1.0, 0.6, 0.42),
            (6, 4, 0.28),
            (20, 18, 0.62),
        };

        public static PixelImage Style(PixelImage window, double scale, bool shadow)
        {
            // Windows 11 paints its own 1px border (and blends the desktop into its corners): drop that ring
            int trim = window.Width > 8 && window.Height > 8 ? 1 : 0;
            var core = trim > 0 ? window.Crop(new System.Windows.Int32Rect(trim, trim, window.Width - 2 * trim, window.Height - 2 * trim)) : window;
            int w = core.Width, h = core.Height;
            double r = Math.Min(Radius * scale, Math.Min(w, h) / 2.0);

            var coverage = new float[w * h];
            var masked = MaskAndRim(core, r, coverage);

            if (!shadow) return new PixelImage(w, h, masked, scale);

            int ml = (int)Math.Round(SideMargin * scale), mt = (int)Math.Round(TopMargin * scale), mb = (int)Math.Round(BottomMargin * scale);
            int pw = w + 2 * ml, ph = h + mt + mb;

            // shadow alpha: blurred, offset copies of the window's coverage, combined like stacked layers
            var shadowA = new float[pw * ph];
            foreach (var (sigma, dy, opacity) in Shadows)
            {
                var layer = new float[pw * ph];
                int oy = mt + (int)Math.Round(dy * scale);
                for (int y = 0; y < h; y++)
                {
                    int ty = y + oy;
                    if (ty < 0 || ty >= ph) continue;
                    Array.Copy(coverage, y * w, layer, ty * pw + ml, w);
                }
                GaussianBlur(layer, pw, ph, sigma * scale);
                float op = (float)opacity;
                for (int i = 0; i < layer.Length; i++)
                    shadowA[i] = 1 - (1 - shadowA[i]) * (1 - layer[i] * op);
            }

            // composite the window over its shadow (premultiplied, shadow is black)
            var px = new byte[pw * ph * 4];
            for (int i = 0; i < shadowA.Length; i++) px[i * 4 + 3] = (byte)Math.Round(Math.Clamp(shadowA[i], 0, 1) * 255);
            for (int y = 0; y < h; y++)
            {
                int src = y * w * 4, dst = ((y + mt) * pw + ml) * 4;
                for (int x = 0; x < w; x++, src += 4, dst += 4)
                {
                    int a = masked[src + 3];
                    if (a == 0) continue;
                    int inv = 255 - a;
                    px[dst] = masked[src];
                    px[dst + 1] = masked[src + 1];
                    px[dst + 2] = masked[src + 2];
                    px[dst + 3] = (byte)(a + (px[dst + 3] * inv + 127) / 255);
                }
            }
            return new PixelImage(pw, ph, px, scale);
        }

        /// <summary>Rounded-rect mask with analytic anti-aliasing plus a 1px rim along the whole edge.</summary>
        static byte[] MaskAndRim(PixelImage img, double r, float[] coverage)
        {
            int w = img.Width, h = img.Height;
            var src = img.Pixels;
            double hw = w / 2.0, hh = h / 2.0;

            double Sd(double x, double y)
            {
                double qx = Math.Abs(x - hw) - (hw - r), qy = Math.Abs(y - hh) - (hh - r);
                double ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
                return Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - r;
            }

            // rim colour follows the window: a light rim on dark windows, a dark one on light windows
            double lumSum = 0; int lumN = 0;
            int step = Math.Max(1, (w + h) / 400);
            for (int x = 0; x < w; x += step) { lumSum += Lum(src, (1 * w + x) * 4) + Lum(src, ((h - 2) * w + x) * 4); lumN += 2; }
            for (int y = 0; y < h; y += step) { lumSum += Lum(src, (y * w + 1) * 4) + Lum(src, (y * w + w - 2) * 4); lumN += 2; }
            bool dark = lumN > 0 && lumSum / lumN < 110;
            byte rim = dark ? (byte)255 : (byte)0;
            float rimAlpha = dark ? 0.20f : 0.16f;

            var dst = new byte[w * h * 4];
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    double sd = Sd(x + 0.5, y + 0.5);
                    float c0 = (float)Math.Clamp(0.5 - sd, 0, 1);        // pixel coverage of the window shape
                    coverage[y * w + x] = c0;
                    if (c0 <= 0) continue;
                    float c1 = (float)Math.Clamp(-0.5 - sd, 0, 1);       // coverage of the shape inset by 1px
                    float k = (c0 - c1) / c0 * rimAlpha;                 // share of this pixel that is rim
                    float b = src[i] + (rim - src[i]) * k, g = src[i + 1] + (rim - src[i + 1]) * k, rr = src[i + 2] + (rim - src[i + 2]) * k;
                    dst[i] = (byte)(b * c0 + 0.5f);
                    dst[i + 1] = (byte)(g * c0 + 0.5f);
                    dst[i + 2] = (byte)(rr * c0 + 0.5f);
                    dst[i + 3] = (byte)(255 * c0 + 0.5f);
                }
            });
            return dst;
        }

        static double Lum(byte[] p, int i) => 0.114 * p[i] + 0.587 * p[i + 1] + 0.299 * p[i + 2];

        /// <summary>Gaussian approximated by three box passes per axis (running sums, O(n)).</summary>
        static void GaussianBlur(float[] d, int w, int h, double sigma)
        {
            if (sigma < 0.3) return;
            double ideal = Math.Sqrt(12 * sigma * sigma / 3 + 1);
            int k = Math.Max(1, (int)Math.Round((ideal - 1) / 2));
            for (int pass = 0; pass < 3; pass++)
            {
                BoxH(d, w, h, k);
                BoxV(d, w, h, k);
            }
        }

        static void BoxH(float[] d, int w, int h, int k)
        {
            float inv = 1f / (2 * k + 1);
            Parallel.For(0, h, y =>
            {
                var line = new float[w];
                int row = y * w;
                Array.Copy(d, row, line, 0, w);
                float sum = 0;
                for (int i = -k; i <= k; i++) if (i >= 0 && i < w) sum += line[i];
                for (int x = 0; x < w; x++)
                {
                    d[row + x] = sum * inv;
                    int add = x + k + 1, rem = x - k;
                    if (add < w) sum += line[add];
                    if (rem >= 0) sum -= line[rem];
                }
            });
        }

        static void BoxV(float[] d, int w, int h, int k)
        {
            float inv = 1f / (2 * k + 1);
            Parallel.For(0, w, x =>
            {
                var col = new float[h];
                for (int y = 0; y < h; y++) col[y] = d[y * w + x];
                float sum = 0;
                for (int i = -k; i <= k; i++) if (i >= 0 && i < h) sum += col[i];
                for (int y = 0; y < h; y++)
                {
                    d[y * w + x] = sum * inv;
                    int add = y + k + 1, rem = y - k;
                    if (add < h) sum += col[add];
                    if (rem >= 0) sum -= col[rem];
                }
            });
        }
    }
}
