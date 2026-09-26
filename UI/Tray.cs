using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Kadr.Core;

namespace Kadr.UI
{
    public sealed class Tray : IDisposable
    {
        readonly NotifyIcon _icon;
        readonly App _app;

        public Tray(App app)
        {
            _app = app;
            Icon ico;
            try
            {
                using var s = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/kadr.ico")).Stream;
                ico = new Icon(s, SystemInformation.SmallIconSize);
            }
            catch { ico = SystemIcons.Application; }

            var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false, ShowCheckMargin = false, Font = new Font("Segoe UI", 9.5f), Padding = new Padding(2, 4, 2, 4) };
            var region = Item("Снимок области", () => _app.StartCapture(false));
            var window = Item("Снимок окна", () => _app.StartCapture(true));
            var full = Item("Весь экран", _app.CaptureFullScreen);
            menu.Items.Add(region);
            menu.Items.Add(window);
            menu.Items.Add(full);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("Открыть папку со снимками", () =>
            {
                Directory.CreateDirectory(Settings.Current.Folder);
                Process.Start("explorer.exe", $"\"{Settings.Current.Folder}\"");
            }));
            menu.Items.Add(Item("Настройки…", SettingsWindow.ShowSingle));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("Выход", () => System.Windows.Application.Current.Shutdown()));
            menu.Opening += (_, _) =>
            {
                SetKeys(region, HotkeyAction.Region);
                SetKeys(window, HotkeyAction.Window);
                SetKeys(full, HotkeyAction.FullScreen);
            };

            _icon = new NotifyIcon { Icon = ico, Text = "Кадр — скриншоты", Visible = true, ContextMenuStrip = menu };
            _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) _app.Dispatcher.BeginInvoke(() => _app.StartCapture(false)); };
        }

        ToolStripMenuItem Item(string text, Action a)
        {
            var i = new ToolStripMenuItem(text) { Padding = new Padding(4, 3, 4, 3) };
            i.Click += (_, _) => _app.Dispatcher.BeginInvoke(a);
            return i;
        }

        static void SetKeys(ToolStripMenuItem item, HotkeyAction a)
        {
            var keys = Hotkeys.FirstFor(a);
            item.ShortcutKeyDisplayString = keys;
            item.ShowShortcutKeys = keys != null;
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }

        sealed class DarkRenderer : ToolStripProfessionalRenderer
        {
            static readonly Color Bg = Color.FromArgb(32, 32, 36);
            static readonly Color Hover = Color.FromArgb(10, 132, 255);
            static readonly Color Text = Color.FromArgb(240, 240, 240);
            static readonly Color Dim = Color.FromArgb(140, 140, 150);

            public DarkRenderer() : base(new DarkColors()) { RoundedEdges = true; }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Bg);

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using var p = new Pen(Color.FromArgb(60, 60, 66));
                e.Graphics.DrawRectangle(p, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected || !e.Item.Enabled) return;
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
                using var path = RoundRect(r, 5);
                using var b = new SolidBrush(Hover);
                g.FillPath(b, path);
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                bool shortcut = e.Text == (e.Item as ToolStripMenuItem)?.ShortcutKeyDisplayString;
                e.TextColor = shortcut && !e.Item.Selected ? Dim : Text;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                using var p = new Pen(Color.FromArgb(55, 55, 62));
                int y = e.Item.Height / 2;
                e.Graphics.DrawLine(p, 10, y, e.Item.Width - 10, y);
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = e.ImageRectangle;
                using var p = new Pen(Text, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
                g.DrawLines(p, new[] { new PointF(cx - 5, cy), new PointF(cx - 1.5f, cy + 3.5f), new PointF(cx + 5, cy - 4) });
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = Text;
                base.OnRenderArrow(e);
            }

            protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

            static GraphicsPath RoundRect(Rectangle r, int rad)
            {
                var p = new GraphicsPath();
                int d = rad * 2;
                p.AddArc(r.X, r.Y, d, d, 180, 90);
                p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                p.CloseFigure();
                return p;
            }
        }

        sealed class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Color.FromArgb(32, 32, 36);
            public override Color ImageMarginGradientBegin => Color.FromArgb(32, 32, 36);
            public override Color ImageMarginGradientMiddle => Color.FromArgb(32, 32, 36);
            public override Color ImageMarginGradientEnd => Color.FromArgb(32, 32, 36);
            public override Color MenuBorder => Color.FromArgb(60, 60, 66);
            public override Color MenuItemBorder => Color.Transparent;
            public override Color MenuItemSelected => Color.FromArgb(10, 132, 255);
            public override Color CheckBackground => Color.Transparent;
            public override Color CheckSelectedBackground => Color.Transparent;
            public override Color CheckPressedBackground => Color.Transparent;
            public override Color ButtonSelectedBorder => Color.Transparent;
        }
    }
}
