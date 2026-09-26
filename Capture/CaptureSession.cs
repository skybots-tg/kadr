using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Kadr.Core;
using Kadr.Editor;

namespace Kadr.Capture
{
    public enum FinishAction { Copy, Save, Pin }

    public sealed class CaptureResult
    {
        public PixelImage Image;                 // final, flattened
        public PixelImage Base;                  // without annotations
        public List<Annotation> Annotations = new();
        public MonitorInfo Monitor;
        public bool IsWindow;
        public FinishAction Action = FinishAction.Copy;
        public string SavedPath;
        public Int32Rect ScreenRect;             // where it was on screen (for pinning in place)
    }

    /// <summary>One capture: a frozen snapshot plus an overlay per monitor.</summary>
    public sealed class CaptureSession
    {
        public readonly ScreenSnapshot Snapshot;
        readonly List<OverlayWindow> _overlays = new();
        readonly Action<CaptureResult> _done;
        bool _closed;

        public bool WindowMode { get; private set; }
        public OverlayWindow Active { get; private set; }
        public static CaptureSession Current { get; private set; }

        public CaptureSession(ScreenSnapshot snap, Action<CaptureResult> done)
        {
            Snapshot = snap;
            _done = done;
        }

        public void Show(bool startInWindowMode = false)
        {
            Current = this;
            WindowMode = startInWindowMode;
            var cursorMon = ScreenCapture.MonitorUnderCursor(Snapshot.Monitors);
            foreach (var m in Snapshot.Monitors)
            {
                var o = new OverlayWindow(this, m, Snapshot.CropMonitor(m));
                _overlays.Add(o);
            }
            foreach (var o in _overlays) o.Show();
            var focus = _overlays.First(o => o.Monitor == cursorMon);
            focus.ActivateHard();
        }

        public void SetWindowMode(bool on)
        {
            WindowMode = on;
            foreach (var o in _overlays) o.OnModeChanged();
        }

        public void BeginSelection(OverlayWindow o)
        {
            if (Active != null && Active != o) Active.ResetToIdle();
            Active = o;
        }

        public void Cancel() => Close();

        public void Complete(CaptureResult r)
        {
            Close();
            _done(r);
        }

        void Close()
        {
            if (_closed) return;
            _closed = true;
            foreach (var o in _overlays) o.CloseQuietly();
            _overlays.Clear();
            if (Current == this) Current = null;
        }

        public WindowInfo WindowAt(int vx, int vy)
        {
            foreach (var w in Snapshot.Windows)
                if (vx >= w.Bounds.X && vx < w.Bounds.X + w.Bounds.Width && vy >= w.Bounds.Y && vy < w.Bounds.Y + w.Bounds.Height)
                    return w;
            return null;
        }

        public void CaptureWindow(WindowInfo w, bool shadow)
        {
            var raw = ScreenCapture.CaptureWindow(Snapshot, w);
            var cx = w.Bounds.X + w.Bounds.Width / 2;
            var cy = w.Bounds.Y + w.Bounds.Height / 2;
            var mon = Snapshot.Monitors.FirstOrDefault(m => cx >= m.Bounds.X && cx < m.Bounds.X + m.Bounds.Width && cy >= m.Bounds.Y && cy < m.Bounds.Y + m.Bounds.Height)
                      ?? Snapshot.Monitors[0];
            var styled = WindowStyler.Style(raw, mon.Scale, shadow);
            Complete(new CaptureResult { Image = styled, Base = styled, Monitor = mon, IsWindow = true, ScreenRect = w.Bounds });
        }
    }
}
