using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kadr.Capture;
using Kadr.Core;
using Kadr.UI;

namespace Kadr
{
    public partial class App : Application
    {
        static Mutex _mutex;
        static bool _ephemeral;
        HotkeyHook _hook;
        Tray _tray;

        const string ShowSettingsEvent = "Kadr.Screenshot.ShowSettings";

        public static App Instance { get; private set; }
        public HotkeyHook Hook => _hook;

        public static ImageSource AppIconImage { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            if (e.Args.Length == 2 && e.Args[0] == "--selftest")
            {
                try { SelfTest.Run(e.Args[1]); } catch (Exception ex) { File.WriteAllText(Path.Combine(e.Args[1], "error.txt"), ex.ToString()); }
                Shutdown();
                return;
            }
            Instance = this;
            base.OnStartup(e);
            DispatcherUnhandledException += (_, ex) => { Log(ex.Exception); ex.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log(ex.ExceptionObject as Exception);

            Settings.Load();
            try
            {
                var s = GetResourceStream(new Uri("pack://application:,,,/Assets/kadr.ico")).Stream;
                var dec = BitmapDecoder.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                AppIconImage = dec.Frames.OrderByDescending(f => f.PixelWidth).First(); // largest frame, crisp at any size
            }
            catch { }

            var args = e.Args;
            bool Has(string a) => Array.Exists(args, x => string.Equals(x, a, StringComparison.OrdinalIgnoreCase));

            if (Has("--promo") && args.Length >= 3)
            {
                _ephemeral = true; // promo overrides must never reach the real settings file
                try { Promo.Run(args[1], args[2]); } catch (Exception ex) { File.WriteAllText(Path.Combine(args[2], "error.txt"), ex.ToString()); }
                Shutdown();
                return;
            }

            if (Has("--test-window") && args.Length >= 3)
            {
                // debug: style a real on-screen window exactly like a Space+click capture
                _ephemeral = true;
                var snap = ScreenCapture.TakeSnapshot();
                var win = snap.Windows.Find(x => x.Title.Contains(args[1], StringComparison.OrdinalIgnoreCase));
                if (win != null)
                {
                    var raw = ScreenCapture.CaptureWindow(snap, win);
                    var mon = ScreenCapture.MonitorUnderCursor(snap.Monitors);
                    File.WriteAllBytes(args[2], Output.EncodePng(WindowStyler.Style(raw, mon.Scale, !Has("--no-shadow"))));
                }
                Shutdown();
                return;
            }
            if (Has("--uninstall"))
            {
                if (Has("--quiet")) Installer.Uninstall();
                else InstallWindow.ForUninstall().ShowDialog();
                Shutdown();
                return;
            }
            if (Has("--install"))
            {
                Installer.Install(autoStart: true);
                Installer.LaunchInstalled();
                Shutdown();
                return;
            }
            // a downloaded single-file build offers to install itself
            if (Installer.IsSingleFile && !Installer.IsInstalledCopy && !Has("--portable"))
            {
                var w = InstallWindow.ForInstall();
                w.ShowDialog();
                if (w.Outcome == InstallWindow.Result.Installed) Installer.LaunchInstalled();
                if (w.Outcome != InstallWindow.Result.Portable) { Shutdown(); return; }
            }

            _mutex = new Mutex(true, "Kadr.Screenshot.SingleInstance", out bool fresh);
            if (!fresh)
            {
                // already running: a second launch (e.g. from Start) opens its settings
                if (EventWaitHandle.TryOpenExisting(ShowSettingsEvent, out var ev)) ev.Set();
                Shutdown();
                return;
            }
            var signal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEvent);
            new Thread(() => { while (signal.WaitOne()) Dispatcher.BeginInvoke(SettingsWindow.ShowSingle); }) { IsBackground = true }.Start();

            _hook = new HotkeyHook(Dispatcher);
            ApplyHotkeys();
            _hook.Start();

            InitAutoStart();
            if (Installer.IsInstalledCopy) { Updater.Cleanup(); Installer.RefreshRegistration(); }
            _tray = new Tray(this);
            StartUpdateLoop();

            WarmUp();
            if (!Settings.Current.FirstRunDone || Has("--welcome"))
            {
                Settings.Current.FirstRunDone = true;
                Settings.Save();
                Welcome.Show();
            }
            if (Has("--updated"))
                Dispatcher.BeginInvoke(() => { Welcome.ShowUpdated(Installer.Version, Settings.Current.UpdateNotes); Settings.Current.UpdateNotes = null; Settings.Save(); },
                    DispatcherPriority.ApplicationIdle);
            if (Has("--settings")) Dispatcher.BeginInvoke(SettingsWindow.ShowSingle, DispatcherPriority.ApplicationIdle);
            if (Has("--capture")) Dispatcher.BeginInvoke(() => StartCapture(false), DispatcherPriority.ApplicationIdle);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _hook?.Dispose();
            _tray?.Dispose();
            if (!_ephemeral) Settings.Save();
            base.OnExit(e);
        }

        /// <summary>JIT the hot paths once so the first real capture appears instantly.</summary>
        void WarmUp()
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    var surface = new Editor.AnnotationSurface();
                    var tb = new Toolbar(surface);
                    tb.Main.Measure(new Size(2000, 200));
                    CursorFactory.Crosshair(1.25);
                    var img = new PixelImage(4, 4, new byte[64], 1);
                    new Loupe(img).Measure(new Size(300, 300));
                    Output.EncodePng(img);
                }
                catch (Exception ex) { Log(ex); }
            }, DispatcherPriority.ApplicationIdle);
        }

        // ------------------------------------------------------------------ over-the-air updates

        DispatcherTimer _updateTimer;
        (Updater.Release release, string path)? _pendingUpdate;
        bool _checking;

        void StartUpdateLoop()
        {
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
            bool first = true;
            _updateTimer.Tick += async (_, _) =>
            {
                if (first) { first = false; if (Installer.IsInstalledCopy) Updater.Cleanup(); } // the previous exe has exited by now
                _updateTimer.Interval = TimeSpan.FromMinutes(_pendingUpdate != null ? 1 : 30);
                if (_pendingUpdate is { } p) { TryApplyWhenIdle(p.release, p.path); return; }
                var s = Settings.Current;
                if (!s.AutoUpdate || !Installer.IsInstalledCopy || _checking) return;
                if (DateTime.Now - s.LastUpdateCheck < TimeSpan.FromHours(6)) return;
                _checking = true;
                try
                {
                    var r = await Updater.FetchLatestAsync();
                    s.LastUpdateCheck = DateTime.Now;
                    Settings.Save();
                    if (Updater.IsNewer(r))
                    {
                        var path = await Updater.DownloadAsync(r);
                        _pendingUpdate = (r, path);
                        _updateTimer.Interval = TimeSpan.FromSeconds(5);
                    }
                }
                catch (Exception ex) { Log(ex); }
                finally { _checking = false; }
            };
            _updateTimer.Start();
        }

        /// <summary>Swap and restart only when nothing is on screen, so an update never interrupts work.</summary>
        void TryApplyWhenIdle(Updater.Release r, string path)
        {
            if (CaptureSession.Current != null || Windows.Count > 0) return;
            ApplyUpdate(r, path);
        }

        public void ApplyUpdate(Updater.Release r, string path)
        {
            try
            {
                Settings.Current.UpdateNotes = Updater.Summary(r.Notes);
                Settings.Save();
                Updater.Apply(path);
                _pendingUpdate = null;
                RestartInto(Installer.InstalledExe, "--updated");
            }
            catch (Exception ex) { Log(ex); _pendingUpdate = null; }
        }

        /// <summary>Hand over to a fresh process: release the hook, tray icon and single-instance lock first.</summary>
        void RestartInto(string exe, string args)
        {
            _updateTimer?.Stop();
            _hook?.Dispose(); _hook = null;
            _tray?.Dispose(); _tray = null;
            try { _mutex?.ReleaseMutex(); _mutex?.Dispose(); _mutex = null; } catch { }
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
            Shutdown();
        }

        public void ApplyHotkeys()
        {
            _hook?.SetBindings(Settings.Current.Hotkeys.Select(b => (b.Vk, b.Mods, (Action)(b.Action switch
            {
                HotkeyAction.Region => () => StartCapture(false),
                HotkeyAction.Window => () => StartCapture(true),
                _ => CaptureFullScreen,
            }))));
        }

        public static void ConfirmUninstall(Window owner)
        {
            var w = InstallWindow.ForUninstall();
            w.Owner = owner;
            w.ShowDialog();
            if (w.Outcome == InstallWindow.Result.Uninstalled) Current.Shutdown();
        }

        public void StartCapture(bool windowMode)
        {
            if (CaptureSession.Current != null)
            {
                CaptureSession.Current.SetWindowMode(!CaptureSession.Current.WindowMode);
                return;
            }
            var snap = ScreenCapture.TakeSnapshot();
            var session = new CaptureSession(snap, HandleResult);
            session.Show(windowMode);
        }

        public void CaptureFullScreen()
        {
            if (CaptureSession.Current != null) return;
            var monitors = ScreenCapture.GetMonitors();
            var m = ScreenCapture.MonitorUnderCursor(monitors);
            var raw = ScreenCapture.CaptureRect(m.Bounds);
            var img = new PixelImage(raw.Width, raw.Height, raw.Pixels, m.Scale);
            HandleResult(new CaptureResult { Image = img, Base = img, Monitor = m, ScreenRect = m.Bounds });
        }

        void HandleResult(CaptureResult r)
        {
            Output.PlayShutter();
            var s = Settings.Current;
            Settings.Save();
            if (r.Action == FinishAction.Pin)
            {
                PinImage(r.Image, r.IsWindow ? null : r.ScreenRect);
                return;
            }
            if (s.CopyToClipboard && r.Action == FinishAction.Copy) Output.CopyToClipboard(r.Image);
            if (s.SaveToFolder && r.SavedPath == null)
            {
                try { r.SavedPath = Output.SaveToFolder(r.Image); } catch (Exception ex) { Log(ex); }
            }
            if (s.ShowThumbnail) new ThumbnailWindow(r).ShowAnimated();
        }

        public static void PinImage(PixelImage img, Int32Rect? at)
        {
            var w = new PinWindow(img, at);
            w.Show();
            Native.ForceForeground(new System.Windows.Interop.WindowInteropHelper(w).Handle);
            w.Activate();
        }

        public static void RevealInExplorer(string path)
        {
            try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
        }

        public static void Log(Exception ex)
        {
            if (ex == null) return;
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kadr");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
            }
            catch { }
        }

        // ------------------------------------------------------------------ autostart

        /// <summary>The installed copy starts with Windows by default; the settings toggle can turn it off for good.</summary>
        static void InitAutoStart()
        {
            if (!Installer.IsInstalledCopy) return;
            try
            {
                if (!Settings.Current.AutoStartInitialized)
                {
                    Installer.AutoStart = true;
                    Settings.Current.AutoStartInitialized = true;
                    Settings.Save();
                }
                else if (Installer.AutoStart) Installer.AutoStart = true; // keep the path current after updates
            }
            catch (Exception ex) { Log(ex); }
        }
    }
}
