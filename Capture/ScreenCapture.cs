using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Kadr.Core;
using static Kadr.Core.Native;

namespace Kadr.Capture
{
    public sealed class MonitorInfo
    {
        public IntPtr Handle;
        public Int32Rect Bounds;     // physical pixels, virtual-screen coordinates
        public Int32Rect WorkArea;
        public double Scale;         // 1.0 = 96 DPI
        public bool Primary;
    }

    public sealed class WindowInfo
    {
        public IntPtr Handle;
        public Int32Rect Bounds;     // visible frame, physical pixels
        public string Title;
        public string ClassName;
        public int ZOrder;
        public bool Maximized;
    }

    /// <summary>Everything grabbed at the instant the hotkey was pressed.</summary>
    public sealed class ScreenSnapshot
    {
        public PixelImage Image;            // whole virtual screen, scale 1 (pixel space)
        public Int32Rect VirtualBounds;
        public List<MonitorInfo> Monitors;
        public List<WindowInfo> Windows;    // top-most first

        public PixelImage CropMonitor(MonitorInfo m)
        {
            var r = new Int32Rect(m.Bounds.X - VirtualBounds.X, m.Bounds.Y - VirtualBounds.Y, m.Bounds.Width, m.Bounds.Height);
            var c = Image.Crop(r);
            return new PixelImage(c.Width, c.Height, c.Pixels, m.Scale);
        }
    }

    public static class ScreenCapture
    {
        public static List<MonitorInfo> GetMonitors()
        {
            var list = new List<MonitorInfo>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr hdc, ref RECT rc, IntPtr d) =>
            {
                var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                GetMonitorInfo(h, ref mi);
                double scale = 1.0;
                if (GetDpiForMonitor(h, 0, out uint dx, out _) == 0 && dx > 0) scale = dx / 96.0;
                list.Add(new MonitorInfo
                {
                    Handle = h,
                    Bounds = new Int32Rect(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Width, mi.rcMonitor.Height),
                    WorkArea = new Int32Rect(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Width, mi.rcWork.Height),
                    Scale = scale,
                    Primary = (mi.dwFlags & 1) != 0,
                });
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static MonitorInfo MonitorUnderCursor(List<MonitorInfo> monitors)
        {
            GetCursorPos(out var p);
            foreach (var m in monitors)
                if (p.X >= m.Bounds.X && p.X < m.Bounds.X + m.Bounds.Width && p.Y >= m.Bounds.Y && p.Y < m.Bounds.Y + m.Bounds.Height)
                    return m;
            return monitors.Find(m => m.Primary) ?? monitors[0];
        }

        public static ScreenSnapshot TakeSnapshot()
        {
            var sw = Stopwatch.StartNew();
            var monitors = GetMonitors();
            int l = int.MaxValue, t = int.MaxValue, r = int.MinValue, b = int.MinValue;
            foreach (var m in monitors)
            {
                l = Math.Min(l, m.Bounds.X); t = Math.Min(t, m.Bounds.Y);
                r = Math.Max(r, m.Bounds.X + m.Bounds.Width); b = Math.Max(b, m.Bounds.Y + m.Bounds.Height);
            }
            var vb = new Int32Rect(l, t, r - l, b - t);
            var img = CaptureRect(vb);
            var windows = EnumerateWindows();
            Debug.WriteLine($"snapshot {sw.ElapsedMilliseconds}ms");
            return new ScreenSnapshot { Image = img, VirtualBounds = vb, Monitors = monitors, Windows = windows };
        }

        public static PixelImage CaptureRect(Int32Rect rect)
        {
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr mem = CreateCompatibleDC(screen);
            var bmi = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = rect.Width, biHeight = -rect.Height,
                biPlanes = 1, biBitCount = 32,
            };
            IntPtr hbmp = CreateDIBSection(screen, ref bmi, DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
            IntPtr old = SelectObject(mem, hbmp);
            BitBlt(mem, 0, 0, rect.Width, rect.Height, screen, rect.X, rect.Y, SRCCOPY | CAPTUREBLT);
            GdiFlush();
            var px = new byte[rect.Width * rect.Height * 4];
            Marshal.Copy(bits, px, 0, px.Length);
            SelectObject(mem, old);
            DeleteObject(hbmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
            for (int i = 3; i < px.Length; i += 4) px[i] = 255;
            return new PixelImage(rect.Width, rect.Height, px, 1.0);
        }

        static readonly HashSet<string> SkipClasses = new HashSet<string>
        {
            "Progman", "WorkerW", "Shell_TrayWnd_Hidden", "SysShadow", "IME", "MSCTFIME UI",
            "Windows.UI.Core.CoreWindow", "ForegroundStaging", "XamlExplorerHostIslandWindow_WASDK",
            "NarratorHelperWindow", "tooltips_class32",
        };

        static readonly HashSet<string> UntitledAllowed = new HashSet<string>
        {
            "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "#32768", "Chrome_WidgetWin_1", "Chrome_WidgetWin_2",
        };

        public static List<WindowInfo> EnumerateWindows()
        {
            var list = new List<WindowInfo>();
            uint myPid = (uint)Environment.ProcessId;
            int z = 0;
            EnumWindows((h, lp) =>
            {
                z++;
                if (!IsWindowVisible(h) || IsIconic(h) || IsCloaked(h)) return true;
                GetWindowThreadProcessId(h, out uint pid);
                if (pid == myPid) return true;
                long ex = GetExStyle(h);
                if ((ex & WS_EX_TRANSPARENT) != 0) return true;
                bool perPixelAlpha = false;
                if ((ex & WS_EX_LAYERED) != 0)
                {
                    if (GetLayeredWindowAttributes(h, out _, out byte alpha, out uint flags)) { if ((flags & 2) != 0 && alpha < 10) return true; }
                    else perPixelAlpha = true;   // UpdateLayeredWindow: the window supplies its own alpha
                }
                string cls = GetClass(h);
                if (SkipClasses.Contains(cls)) return true;
                string title = GetTitle(h);
                var rc = GetVisibleBounds(h);
                if (rc.Width < 24 || rc.Height < 24) return true;
                if (title.Length == 0 && !UntitledAllowed.Contains(cls)) return true;
                if (title.Length == 0 && cls.StartsWith("Chrome_WidgetWin") && (rc.Width < 60 || rc.Height < 40)) return true;
                if (perPixelAlpha) rc = TrimShadowMargin(h, rc);
                list.Add(new WindowInfo
                {
                    Handle = h,
                    Bounds = new Int32Rect(rc.Left, rc.Top, rc.Width, rc.Height),
                    Title = title,
                    ClassName = cls,
                    ZOrder = z,
                    Maximized = IsZoomed(h),
                });
                return true;
            }, IntPtr.Zero);
            return list;
        }

        /// <summary>
        /// Frameless windows with per-pixel alpha (Telegram mini apps and the like) paint their own shadow into a
        /// transparent margin that still counts as the window, so the desktop would show through the shot. Rendered
        /// on its own that margin comes out pure black (the shadow is black, premultiplied): peel off all-black edge
        /// lines, but only when every side has them — otherwise it is black content, not a shadow.
        /// </summary>
        static RECT TrimShadowMargin(IntPtr hwnd, RECT rc)
        {
            var img = PrintWindowImage(hwnd, new Int32Rect(rc.Left, rc.Top, rc.Width, rc.Height));
            if (img == null || img.Width != rc.Width || img.Height != rc.Height) return rc;
            int w = img.Width, h = img.Height, cap = Math.Min(w, h) / 8;
            var p = img.Pixels;
            bool Black(int x, int y) { int i = (y * w + x) * 4; return (p[i] | p[i + 1] | p[i + 2]) == 0; }
            bool RowBlack(int y) { for (int x = 0; x < w; x++) if (!Black(x, y)) return false; return true; }
            bool ColBlack(int x, int y0, int y1) { for (int y = y0; y < y1; y++) if (!Black(x, y)) return false; return true; }

            int top = 0, bottom = 0, left = 0, right = 0;
            while (top < cap && RowBlack(top)) top++;
            while (bottom < cap && RowBlack(h - 1 - bottom)) bottom++;
            while (left < cap && ColBlack(left, top, h - bottom)) left++;
            while (right < cap && ColBlack(w - 1 - right, top, h - bottom)) right++;
            if (top == 0 || bottom == 0 || left == 0 || right == 0) return rc;
            return new RECT { Left = rc.Left + left, Top = rc.Top + top, Right = rc.Right - right, Bottom = rc.Bottom - bottom };
        }

        static bool Intersects(Int32Rect a, Int32Rect b) =>
            a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

        static bool Contains(Int32Rect outer, Int32Rect inner) =>
            inner.X >= outer.X && inner.Y >= outer.Y && inner.X + inner.Width <= outer.X + outer.Width && inner.Y + inner.Height <= outer.Y + outer.Height;

        /// <summary>
        /// Pixels of a single window. Uses the frozen snapshot when the window is fully visible
        /// (pixel-exact, what the user saw), otherwise asks the window to render itself.
        /// </summary>
        public static PixelImage CaptureWindow(ScreenSnapshot snap, WindowInfo w)
        {
            var bounds = w.Bounds;
            // A maximized window's frame may extend past its monitor: clip to it.
            foreach (var m in snap.Monitors)
                if (Intersects(m.Bounds, bounds) && w.Maximized)
                {
                    int x = Math.Max(bounds.X, m.Bounds.X), y = Math.Max(bounds.Y, m.Bounds.Y);
                    int r = Math.Min(bounds.X + bounds.Width, m.Bounds.X + m.Bounds.Width);
                    int b = Math.Min(bounds.Y + bounds.Height, m.Bounds.Y + m.Bounds.Height);
                    bounds = new Int32Rect(x, y, r - x, b - y);
                    break;
                }

            bool occluded = false;
            foreach (var o in snap.Windows)
            {
                if (o == w) break;
                if (Intersects(o.Bounds, bounds)) { occluded = true; break; }
            }

            if (!occluded && Contains(snap.VirtualBounds, bounds))
            {
                var r = new Int32Rect(bounds.X - snap.VirtualBounds.X, bounds.Y - snap.VirtualBounds.Y, bounds.Width, bounds.Height);
                return snap.Image.Crop(r);
            }

            var viaPrint = PrintWindowImage(w.Handle, bounds);
            if (viaPrint != null) return viaPrint;
            var fallback = new Int32Rect(bounds.X - snap.VirtualBounds.X, bounds.Y - snap.VirtualBounds.Y, bounds.Width, bounds.Height);
            return snap.Image.Crop(fallback);
        }

        static PixelImage PrintWindowImage(IntPtr hwnd, Int32Rect visible)
        {
            if (!GetWindowRect(hwnd, out RECT wr) || wr.Width <= 0 || wr.Height <= 0) return null;
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr mem = CreateCompatibleDC(screen);
            var bmi = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = wr.Width, biHeight = -wr.Height, biPlanes = 1, biBitCount = 32,
            };
            IntPtr hbmp = CreateDIBSection(screen, ref bmi, DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
            IntPtr old = SelectObject(mem, hbmp);
            bool ok = PrintWindow(hwnd, mem, PW_RENDERFULLCONTENT);
            GdiFlush();
            byte[] px = null;
            if (ok)
            {
                px = new byte[wr.Width * wr.Height * 4];
                Marshal.Copy(bits, px, 0, px.Length);
            }
            SelectObject(mem, old);
            DeleteObject(hbmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
            if (px == null) return null;

            bool allBlack = true;
            for (int i = 0; i < px.Length; i += 4)
            {
                px[i + 3] = 255;
                if (allBlack && (px[i] | px[i + 1] | px[i + 2]) != 0) allBlack = false;
            }
            if (allBlack) return null;
            var full = new PixelImage(wr.Width, wr.Height, px, 1.0);
            return full.Crop(new Int32Rect(visible.X - wr.Left, visible.Y - wr.Top, visible.Width, visible.Height));
        }
    }
}
