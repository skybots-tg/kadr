using System;
using System.IO;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kadr.Core
{
    public static class Output
    {
        public static byte[] EncodePng(PixelImage img)
        {
            var enc = new PngBitmapEncoder();
            var src = BitmapSource.Create(img.Width, img.Height, 96 * img.Scale, 96 * img.Scale,
                img.HasTransparency() ? PixelFormats.Pbgra32 : PixelFormats.Bgr32, null, img.Pixels, img.Stride);
            enc.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }

        public static void CopyToClipboard(PixelImage img)
        {
            var data = new DataObject();
            data.SetImage(img.FlattenOn(Colors.White));
            data.SetData("PNG", new MemoryStream(EncodePng(img)), false);
            for (int i = 0; i < 10; i++)
            {
                try { Clipboard.SetDataObject(data, true); return; }
                catch { Thread.Sleep(40); }
            }
        }

        public static string NewFileName()
        {
            var now = DateTime.Now;
            return $"Снимок экрана {now:yyyy-MM-dd} в {now:HH.mm.ss}";
        }

        public static string SaveToFolder(PixelImage img, string folder = null)
        {
            folder ??= Settings.Current.Folder;
            Directory.CreateDirectory(folder);
            string name = NewFileName();
            string path = Path.Combine(folder, name + ".png");
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{name} ({i}).png");
            File.WriteAllBytes(path, EncodePng(img));
            return path;
        }

        public static string SaveTemp(PixelImage img)
        {
            string dir = Path.Combine(Path.GetTempPath(), "Kadr");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, NewFileName() + ".png");
            File.WriteAllBytes(path, EncodePng(img));
            return path;
        }

        public static string SaveAs(PixelImage img, Window owner)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = NewFileName(),
                DefaultExt = ".png",
                Filter = "PNG|*.png|JPEG|*.jpg",
                InitialDirectory = Settings.Current.Folder,
            };
            if (dlg.ShowDialog(owner) != true) return null;
            if (dlg.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                var enc = new JpegBitmapEncoder { QualityLevel = 92 };
                enc.Frames.Add(BitmapFrame.Create(img.FlattenOn(Colors.White)));
                using var fs = File.Create(dlg.FileName);
                enc.Save(fs);
            }
            else File.WriteAllBytes(dlg.FileName, EncodePng(img));
            return dlg.FileName;
        }

        static byte[] _shutter;

        /// <summary>Soft synthesized camera shutter (two filtered clicks).</summary>
        public static void PlayShutter()
        {
            if (!Settings.Current.PlaySound) return;
            _shutter ??= SynthShutter();
            Task.Run(() =>
            {
                try { using var p = new SoundPlayer(new MemoryStream(_shutter)); p.PlaySync(); } catch { }
            });
        }

        static byte[] SynthShutter()
        {
            const int rate = 44100;
            int n = (int)(rate * 0.16);
            var samples = new short[n];
            var rnd = new Random(7);
            double lp = 0, lp2 = 0;
            void Click(int start, double amp, double decay, double tone)
            {
                for (int i = start; i < n; i++)
                {
                    double t = (i - start) / (double)rate;
                    double env = Math.Exp(-t * decay);
                    if (env < 0.001) break;
                    double noise = rnd.NextDouble() * 2 - 1;
                    lp += (noise - lp) * 0.35;
                    lp2 += (lp - lp2) * 0.5;
                    double body = Math.Sin(2 * Math.PI * tone * t) * 0.35;
                    double v = (lp2 * 0.9 + body) * env * amp;
                    samples[i] = (short)Math.Clamp(samples[i] + v * 32767, short.MinValue, short.MaxValue);
                }
            }
            Click(0, 0.55, 90, 1900);
            Click((int)(rate * 0.055), 0.42, 70, 1300);

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write("RIFF"u8.ToArray()); w.Write(36 + n * 2); w.Write("WAVE"u8.ToArray());
            w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8.ToArray()); w.Write(n * 2);
            foreach (var s in samples) w.Write(s);
            return ms.ToArray();
        }
    }
}
