using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kadr.Core;

namespace Kadr.Editor
{
    /// <summary>Pixel-level effects computed on the captured image (deterministic on screen and in export).</summary>
    public static class BlurMath
    {
        static readonly ConditionalWeakTable<PixelImage, Dictionary<int, BitmapSource>> FullCache = new();

        public static BitmapSource Pixelate(PixelImage img, Int32Rect r, int block)
        {
            int w = r.Width, h = r.Height;
            var dst = new byte[w * h * 4];
            var src = img.Pixels;
            for (int by = 0; by < h; by += block)
                for (int bx = 0; bx < w; bx += block)
                {
                    int bw = Math.Min(block, w - bx), bh = Math.Min(block, h - by);
                    long sb = 0, sg = 0, sr = 0, sa = 0;
                    for (int yy = 0; yy < bh; yy++)
                    {
                        int i = ((r.Y + by + yy) * img.Width + r.X + bx) * 4;
                        for (int xx = 0; xx < bw; xx++, i += 4) { sb += src[i]; sg += src[i + 1]; sr += src[i + 2]; sa += src[i + 3]; }
                    }
                    int n = bw * bh;
                    byte cb = (byte)(sb / n), cg = (byte)(sg / n), cr = (byte)(sr / n), ca = (byte)(sa / n);
                    for (int yy = 0; yy < bh; yy++)
                    {
                        int i = ((by + yy) * w + bx) * 4;
                        for (int xx = 0; xx < bw; xx++, i += 4) { dst[i] = cb; dst[i + 1] = cg; dst[i + 2] = cr; dst[i + 3] = ca; }
                    }
                }
            return ToBitmap(dst, w, h);
        }

        /// <summary>Near-gaussian blur: downsample, three box passes, bilinear upsample.</summary>
        public static BitmapSource Gaussian(PixelImage img, Int32Rect r, int radius)
        {
            int f = Math.Max(1, radius / 3);
            int w = r.Width, h = r.Height;
            int sw = Math.Max(1, (w + f - 1) / f), sh = Math.Max(1, (h + f - 1) / f);
            var small = new float[sw * sh * 4];
            var src = img.Pixels;
            for (int y = 0; y < sh; y++)
                for (int x = 0; x < sw; x++)
                {
                    int x0 = x * f, y0 = y * f;
                    int bw = Math.Min(f, w - x0), bh = Math.Min(f, h - y0);
                    float b = 0, g = 0, rr = 0, a = 0;
                    for (int yy = 0; yy < bh; yy++)
                    {
                        int i = ((r.Y + y0 + yy) * img.Width + r.X + x0) * 4;
                        for (int xx = 0; xx < bw; xx++, i += 4) { b += src[i]; g += src[i + 1]; rr += src[i + 2]; a += src[i + 3]; }
                    }
                    float n = bw * bh;
                    int o = (y * sw + x) * 4;
                    small[o] = b / n; small[o + 1] = g / n; small[o + 2] = rr / n; small[o + 3] = a / n;
                }

            int k = Math.Max(1, (int)Math.Round(radius / (double)f / 1.6));
            for (int pass = 0; pass < 3; pass++)
            {
                BoxH(small, sw, sh, k);
                BoxV(small, sw, sh, k);
            }

            var dst = new byte[w * h * 4];
            var xi0 = new int[w]; var xi1 = new int[w]; var xt = new float[w];
            for (int x = 0; x < w; x++)
            {
                float fx = Math.Clamp((x + 0.5f) / f - 0.5f, 0, sw - 1);
                xi0[x] = (int)fx; xi1[x] = Math.Min(xi0[x] + 1, sw - 1); xt[x] = fx - xi0[x];
            }
            for (int y = 0; y < h; y++)
            {
                float fy = Math.Clamp((y + 0.5f) / f - 0.5f, 0, sh - 1);
                int y0 = (int)fy, y1 = Math.Min(y0 + 1, sh - 1);
                float ty = fy - y0;
                int row0 = y0 * sw * 4, row1 = y1 * sw * 4;
                int o = y * w * 4;
                for (int x = 0; x < w; x++, o += 4)
                {
                    int a0 = row0 + xi0[x] * 4, a1 = row0 + xi1[x] * 4, b0 = row1 + xi0[x] * 4, b1 = row1 + xi1[x] * 4;
                    float tx = xt[x];
                    for (int c = 0; c < 4; c++)
                    {
                        float top = small[a0 + c] + (small[a1 + c] - small[a0 + c]) * tx;
                        float bot = small[b0 + c] + (small[b1 + c] - small[b0 + c]) * tx;
                        dst[o + c] = (byte)Math.Clamp(top + (bot - top) * ty + 0.5f, 0, 255);
                    }
                }
            }
            return ToBitmap(dst, w, h);
        }

        public static BitmapSource FullBlur(PixelImage img, int radius)
        {
            var map = FullCache.GetOrCreateValue(img);
            lock (map)
            {
                if (!map.TryGetValue(radius, out var bmp))
                {
                    bmp = Gaussian(img, new Int32Rect(0, 0, img.Width, img.Height), radius);
                    map[radius] = bmp;
                }
                return bmp;
            }
        }

        static void BoxH(float[] d, int w, int h, int k)
        {
            var line = new float[w * 4];
            float inv = 1f / (2 * k + 1);
            for (int y = 0; y < h; y++)
            {
                int row = y * w * 4;
                Array.Copy(d, row, line, 0, w * 4);
                for (int c = 0; c < 4; c++)
                {
                    float sum = 0;
                    for (int i = -k; i <= k; i++) sum += line[Math.Clamp(i, 0, w - 1) * 4 + c];
                    for (int x = 0; x < w; x++)
                    {
                        d[row + x * 4 + c] = sum * inv;
                        sum += line[Math.Min(x + k + 1, w - 1) * 4 + c] - line[Math.Max(x - k, 0) * 4 + c];
                    }
                }
            }
        }

        static void BoxV(float[] d, int w, int h, int k)
        {
            var col = new float[h * 4];
            float inv = 1f / (2 * k + 1);
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                    for (int c = 0; c < 4; c++) col[y * 4 + c] = d[(y * w + x) * 4 + c];
                for (int c = 0; c < 4; c++)
                {
                    float sum = 0;
                    for (int i = -k; i <= k; i++) sum += col[Math.Clamp(i, 0, h - 1) * 4 + c];
                    for (int y = 0; y < h; y++)
                    {
                        d[(y * w + x) * 4 + c] = sum * inv;
                        sum += col[Math.Min(y + k + 1, h - 1) * 4 + c] - col[Math.Max(y - k, 0) * 4 + c];
                    }
                }
            }
        }

        static BitmapSource ToBitmap(byte[] px, int w, int h)
        {
            var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, w * 4);
            b.Freeze();
            return b;
        }
    }
}
