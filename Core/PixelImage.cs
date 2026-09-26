using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kadr.Core
{
    /// <summary>
    /// Raw premultiplied BGRA image plus the DPI scale it was captured at.
    /// "DIP" coordinates used by annotations = pixels / Scale.
    /// </summary>
    public sealed class PixelImage
    {
        public readonly int Width, Height;
        public readonly byte[] Pixels;
        public readonly double Scale;
        private BitmapSource _bitmap;

        public PixelImage(int w, int h, byte[] pixels, double scale)
        {
            Width = w; Height = h; Pixels = pixels; Scale = scale;
        }

        public int Stride => Width * 4;
        public double DipWidth => Width / Scale;
        public double DipHeight => Height / Scale;

        /// <summary>Bitmap with 96 DPI (1 bitmap DIP == 1 pixel).</summary>
        public BitmapSource Bitmap
        {
            get
            {
                if (_bitmap == null)
                {
                    var b = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Pbgra32, null, Pixels, Stride);
                    b.Freeze();
                    _bitmap = b;
                }
                return _bitmap;
            }
        }

        /// <summary>Bitmap tagged with the capture DPI so it renders at its DIP size.</summary>
        public BitmapSource ScaledBitmap()
        {
            var b = BitmapSource.Create(Width, Height, 96 * Scale, 96 * Scale, PixelFormats.Pbgra32, null, Pixels, Stride);
            b.Freeze();
            return b;
        }

        public PixelImage Crop(Int32Rect r)
        {
            r = Clamp(r);
            var dst = new byte[r.Width * r.Height * 4];
            for (int y = 0; y < r.Height; y++)
                Buffer.BlockCopy(Pixels, ((r.Y + y) * Width + r.X) * 4, dst, y * r.Width * 4, r.Width * 4);
            return new PixelImage(r.Width, r.Height, dst, Scale);
        }

        public Int32Rect Clamp(Int32Rect r)
        {
            int x = Math.Max(0, r.X), y = Math.Max(0, r.Y);
            int x2 = Math.Min(Width, r.X + r.Width), y2 = Math.Min(Height, r.Y + r.Height);
            return new Int32Rect(x, y, Math.Max(1, x2 - x), Math.Max(1, y2 - y));
        }

        public Color GetPixel(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return Colors.Transparent;
            int i = (y * Width + x) * 4;
            return Color.FromArgb(Pixels[i + 3], Pixels[i + 2], Pixels[i + 1], Pixels[i]);
        }

        public static PixelImage FromBitmap(BitmapSource src, double scale)
        {
            if (src.Format != PixelFormats.Pbgra32)
                src = new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
            int w = src.PixelWidth, h = src.PixelHeight;
            var px = new byte[w * h * 4];
            src.CopyPixels(px, w * 4, 0);
            return new PixelImage(w, h, px, scale);
        }

        /// <summary>Alpha-composite a premultiplied overlay (same size) on top of this image.</summary>
        public PixelImage CompositeOver(byte[] overlay)
        {
            var dst = (byte[])Pixels.Clone();
            for (int i = 0; i < dst.Length; i += 4)
            {
                int a = overlay[i + 3];
                if (a == 0) continue;
                if (a == 255)
                {
                    dst[i] = overlay[i]; dst[i + 1] = overlay[i + 1]; dst[i + 2] = overlay[i + 2]; dst[i + 3] = 255;
                    continue;
                }
                int inv = 255 - a;
                dst[i] = (byte)(overlay[i] + (dst[i] * inv + 127) / 255);
                dst[i + 1] = (byte)(overlay[i + 1] + (dst[i + 1] * inv + 127) / 255);
                dst[i + 2] = (byte)(overlay[i + 2] + (dst[i + 2] * inv + 127) / 255);
                dst[i + 3] = (byte)(a + (dst[i + 3] * inv + 127) / 255);
            }
            return new PixelImage(Width, Height, dst, Scale);
        }

        /// <summary>Opaque copy composited over a solid color (for apps that ignore alpha).</summary>
        public BitmapSource FlattenOn(Color bg)
        {
            var dst = new byte[Pixels.Length];
            for (int i = 0; i < dst.Length; i += 4)
            {
                int inv = 255 - Pixels[i + 3];
                dst[i] = (byte)(Pixels[i] + bg.B * inv / 255);
                dst[i + 1] = (byte)(Pixels[i + 1] + bg.G * inv / 255);
                dst[i + 2] = (byte)(Pixels[i + 2] + bg.R * inv / 255);
                dst[i + 3] = 255;
            }
            var b = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, dst, Stride);
            b.Freeze();
            return b;
        }

        public bool HasTransparency()
        {
            for (int i = 3; i < Pixels.Length; i += 4)
                if (Pixels[i] != 255) return true;
            return false;
        }
    }
}
