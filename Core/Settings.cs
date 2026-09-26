using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Kadr.Core
{
    public sealed class Settings
    {
        public bool SaveToFolder { get; set; } = true;
        public string Folder { get; set; } = DefaultFolder();
        public bool CopyToClipboard { get; set; } = true;
        public bool WindowShadow { get; set; } = true;
        public bool PlaySound { get; set; } = true;
        public bool ShowThumbnail { get; set; } = true;
        public bool ShowMagnifier { get; set; } = true;
        public bool FirstRunDone { get; set; }

        public string LastColor { get; set; } = "#FF3B30";
        public int LastSize { get; set; } = 1;
        public int TextStyle { get; set; } = 0;
        public int BlurKind { get; set; } = 1;
        public bool AutoStartInitialized { get; set; }
        public List<HotkeyBinding> Hotkeys { get; set; }
        public bool HotkeysCustomized { get; set; }

        static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kadr");
        static string FilePath => Path.Combine(Dir, "settings.json");

        public static Settings Current { get; private set; } = new Settings();

        static string DefaultFolder() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    Current = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            }
            catch { Current = new Settings(); }
            if (string.IsNullOrWhiteSpace(Current.Folder)) Current.Folder = DefaultFolder();
            Kadr.Core.Hotkeys.Normalize(Current);
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
