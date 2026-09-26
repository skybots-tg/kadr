using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;

namespace Kadr.Core
{
    /// <summary>
    /// Per-user install without admin rights or external tools: the downloaded Kadr.exe copies itself to
    /// %LOCALAPPDATA%\Programs\Kadr, adds a Start menu shortcut and an "Apps &amp; features" entry.
    /// </summary>
    public static class Installer
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Kadr";

        public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Kadr");
        public static string InstalledExe => Path.Combine(InstallDir, "Kadr.exe");
        static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Кадр.lnk");

        public static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        public static bool IsInstalledCopy =>
            string.Equals(Path.GetFullPath(Environment.ProcessPath ?? ""), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

        /// <summary>A self-contained single-file build has no Kadr.dll beside it.</summary>
        public static bool IsSingleFile => !File.Exists(Path.Combine(AppContext.BaseDirectory, "Kadr.dll"));

        public static bool IsInstalled => File.Exists(InstalledExe);

        // ------------------------------------------------------------------ autostart

        public static bool AutoStart
        {
            get
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey);
                return k?.GetValue("Kadr") is string;
            }
            set
            {
                using var k = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value) k.SetValue("Kadr", $"\"{(IsInstalled ? InstalledExe : Environment.ProcessPath)}\"");
                else k.DeleteValue("Kadr", false);
            }
        }

        // ------------------------------------------------------------------ install

        public static void Install(bool autoStart)
        {
            StopOtherInstances();
            Directory.CreateDirectory(InstallDir);
            // leftovers from older multi-file builds would confuse the single-file host
            foreach (var f in Directory.GetFiles(InstallDir))
                if (!string.Equals(Path.GetFileName(f), "Kadr.exe", StringComparison.OrdinalIgnoreCase))
                    TryDelete(f);
            File.Copy(Environment.ProcessPath!, InstalledExe, true);

            Register();

            AutoStart = autoStart;
            Settings.Current.AutoStartInitialized = true;
            Settings.Save();
        }

        /// <summary>Start menu shortcut and the "Apps &amp; features" entry for the installed exe.</summary>
        static void Register()
        {
            CreateShortcut(ShortcutPath, InstalledExe, "Кадр — скриншоты как на Mac");

            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "Кадр");
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", "Kadr");
                k.SetValue("DisplayIcon", $"\"{InstalledExe}\",0");
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
                k.SetValue("QuietUninstallString", $"\"{InstalledExe}\" --uninstall --quiet");
                k.SetValue("URLInfoAbout", "https://github.com/skybots-tg/kadr");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
            }
        }

        /// <summary>Start the installed copy through Explorer so it runs as a normal, independent process.</summary>
        public static void LaunchInstalled(string args = null)
        {
            if (args == null) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{InstalledExe}\"") { UseShellExecute = false });
            else Process.Start(new ProcessStartInfo(InstalledExe, args) { UseShellExecute = true, WorkingDirectory = InstallDir });
        }

        /// <summary>
        /// Keep the shortcut and the "Apps &amp; features" entry present and current — after an over-the-air
        /// update, or if something removed them.
        /// </summary>
        public static void RefreshRegistration()
        {
            if (!IsInstalledCopy) return;
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(UninstallKey);
                if (k == null || (k.GetValue("DisplayVersion") as string) != Version || !File.Exists(ShortcutPath)) Register();
            }
            catch { }
        }

        // ------------------------------------------------------------------ uninstall

        public static void Uninstall()
        {
            StopOtherInstances();
            AutoStart = false;
            TryDelete(ShortcutPath);
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kadr"), true); } catch { }
            // the running exe can't delete itself: let cmd remove the folder once we've exited
            var cmd = $"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{InstallDir}\"";
            Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        }

        // ------------------------------------------------------------------ helpers

        static void StopOtherInstances()
        {
            int me = Environment.ProcessId;
            foreach (var p in Process.GetProcessesByName("Kadr"))
            {
                if (p.Id == me) continue;
                try
                {
                    string path = p.MainModule?.FileName;
                    if (path != null && Path.GetFullPath(path).StartsWith(InstallDir, StringComparison.OrdinalIgnoreCase))
                    {
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                }
                catch { }
            }
        }

        static void CreateShortcut(string lnk, string target, string description)
        {
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return;
                dynamic shell = Activator.CreateInstance(type);
                dynamic sc = shell.CreateShortcut(lnk);
                sc.TargetPath = target;
                sc.WorkingDirectory = Path.GetDirectoryName(target);
                sc.Description = description;
                sc.IconLocation = target + ",0";
                sc.Save();
            }
            catch { }
        }

        static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }
    }
}
