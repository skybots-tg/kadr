using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace Kadr.Core
{
    public enum HotkeyAction { Region, Window, FullScreen }

    public sealed class HotkeyBinding
    {
        public HotkeyAction Action { get; set; }
        public int Vk { get; set; }
        public Mods Mods { get; set; }

        public HotkeyBinding() { }
        public HotkeyBinding(HotkeyAction a, int vk, Mods m) { Action = a; Vk = vk; Mods = m; }

        public bool SameKeys(HotkeyBinding o) => o != null && o.Vk == Vk && o.Mods == Mods;
        public override string ToString() => Hotkeys.Format(Vk, Mods);
    }

    public static class Hotkeys
    {
        public static List<HotkeyBinding> Defaults() => new()
        {
            new(HotkeyAction.Region, 0x2C, Mods.None),
            new(HotkeyAction.Region, 0x34, Mods.Ctrl | Mods.Shift),
            new(HotkeyAction.Window, 0x35, Mods.Ctrl | Mods.Shift),
            new(HotkeyAction.FullScreen, 0x2C, Mods.Shift),
            new(HotkeyAction.FullScreen, 0x33, Mods.Ctrl | Mods.Shift),
        };

        public static string Title(HotkeyAction a) => a switch
        {
            HotkeyAction.Region => "Снимок области",
            HotkeyAction.Window => "Снимок окна",
            _ => "Весь экран",
        };

        public static bool IsModifier(int vk) => vk is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C;

        static readonly Dictionary<int, string> Names = new()
        {
            [0x2C] = "PrtSc", [0x13] = "Pause", [0x91] = "Scroll Lock", [0x2D] = "Insert", [0x2E] = "Delete",
            [0x24] = "Home", [0x23] = "End", [0x21] = "PgUp", [0x22] = "PgDn", [0x20] = "Пробел", [0x09] = "Tab",
            [0x0D] = "Enter", [0x08] = "Backspace", [0x1B] = "Esc", [0x25] = "←", [0x26] = "↑", [0x27] = "→", [0x28] = "↓",
            [0xC0] = "`", [0xBD] = "-", [0xBB] = "=", [0xDB] = "[", [0xDD] = "]", [0xDC] = "\\", [0xBA] = ";", [0xDE] = "'",
            [0xBC] = ",", [0xBE] = ".", [0xBF] = "/", [0x6A] = "Num *", [0x6B] = "Num +", [0x6D] = "Num -", [0x6F] = "Num /",
        };

        public static string KeyName(int vk)
        {
            if (Names.TryGetValue(vk, out var n)) return n;
            if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)vk).ToString();
            if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);
            if (vk is >= 0x60 and <= 0x69) return "Num " + (vk - 0x60);
            var key = KeyInterop.KeyFromVirtualKey(vk);
            return key == Key.None ? $"0x{vk:X2}" : key.ToString();
        }

        public static IEnumerable<string> Parts(int vk, Mods m)
        {
            if (m.HasFlag(Mods.Ctrl)) yield return "Ctrl";
            if (m.HasFlag(Mods.Alt)) yield return "Alt";
            if (m.HasFlag(Mods.Shift)) yield return "Shift";
            if (m.HasFlag(Mods.Win)) yield return "Win";
            yield return KeyName(vk);
        }

        public static string Format(int vk, Mods m) => string.Join("+", Parts(vk, m));

        /// <summary>Shortcut text for menus, e.g. "PrtSc" — the first binding of an action.</summary>
        public static string FirstFor(HotkeyAction a) =>
            Settings.Current.Hotkeys.FirstOrDefault(b => b.Action == a)?.ToString();

        public static void Normalize(Settings s)
        {
            if (s.Hotkeys == null || s.Hotkeys.Count == 0 && !s.HotkeysCustomized) s.Hotkeys = Defaults();
            s.Hotkeys = s.Hotkeys.Where(b => b != null && b.Vk > 0 && !IsModifier(b.Vk)).ToList();
        }
    }
}
