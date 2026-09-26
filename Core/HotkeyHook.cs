using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;
using static Kadr.Core.Native;

namespace Kadr.Core
{
    [Flags]
    public enum Mods { None = 0, Ctrl = 1, Shift = 2, Alt = 4, Win = 8 }

    /// <summary>
    /// Global hotkeys through a low-level keyboard hook running on its own thread,
    /// so a busy UI thread can never make Windows drop the hook. Matching keys are swallowed.
    /// </summary>
    public sealed class HotkeyHook : IDisposable
    {
        readonly Dispatcher _ui;
        volatile (int vk, Mods mods, Action action)[] _bindings = Array.Empty<(int, Mods, Action)>();
        readonly HashSet<int> _swallowedDown = new();
        volatile Action<int, Mods> _recorder;
        Thread _thread;
        uint _threadId;
        IntPtr _hook;
        LowLevelKeyboardProc _proc;

        public HotkeyHook(Dispatcher ui) { _ui = ui; }

        public void SetBindings(IEnumerable<(int vk, Mods mods, Action action)> bindings) => _bindings = new List<(int, Mods, Action)>(bindings).ToArray();

        /// <summary>Capture the next non-modifier key press (with its modifiers) instead of running hotkeys.</summary>
        public void Record(Action<int, Mods> onKey) => _recorder = onKey;
        public void StopRecording() => _recorder = null;

        public void Start()
        {
            var ready = new ManualResetEventSlim();
            _thread = new Thread(() =>
            {
                _threadId = GetCurrentThreadId();
                _proc = HookProc;
                _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
                ready.Set();
                while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0) { }
                if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
            }) { IsBackground = true, Name = "Kadr keyboard hook" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            ready.Wait(2000);
        }

        static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        static Mods CurrentMods()
        {
            var m = Mods.None;
            if (Down(0x11)) m |= Mods.Ctrl;
            if (Down(0x10)) m |= Mods.Shift;
            if (Down(0x12)) m |= Mods.Alt;
            if (Down(0x5B) || Down(0x5C)) m |= Mods.Win;
            return m;
        }

        IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                var k = System.Runtime.InteropServices.Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int msg = wParam.ToInt32();
                int vk = (int)k.vkCode;
                if (vk != 0xE8) // our own foreground-unlock key
                {
                    bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                    if (down)
                    {
                        var rec = _recorder;
                        if (rec != null && !Hotkeys.IsModifier(vk))
                        {
                            _recorder = null;
                            var mods = CurrentMods();
                            _swallowedDown.Add(vk);
                            _ui.BeginInvoke(rec, vk, mods);
                            return (IntPtr)1;
                        }
                        if (rec == null)
                        {
                            var mods = CurrentMods();
                            foreach (var b in _bindings)
                            {
                                if (b.vk == vk && b.mods == mods)
                                {
                                    if (_swallowedDown.Add(vk)) // ignore auto-repeat
                                        _ui.BeginInvoke(b.action, DispatcherPriority.Send);
                                    return (IntPtr)1;
                                }
                            }
                        }
                    }
                    else if ((msg == WM_KEYUP || msg == WM_SYSKEYUP) && _swallowedDown.Remove(vk))
                    {
                        return (IntPtr)1;
                    }
                }
            }
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        public void Dispose()
        {
            if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
