using System.Diagnostics;
using System.Runtime.InteropServices;
using static StratagemHelper.Native;

namespace StratagemHelper;

/// <summary>
/// Global low-level keyboard + mouse hooks. Handles Ctrl+1..9 / Ctrl+C / Ctrl+V, one-touch bindings (keys, middle and
/// side mouse buttons, pedals), push-to-talk state, binding capture for the settings UI, and physical Ctrl
/// up/down events. All callbacks run on the UI thread and must return quickly.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    private readonly LowLevelKeyboardProc _kbProc;   // keep references so the GC doesn't collect them
    private readonly LowLevelKeyboardProc _mouseProc;
    private IntPtr _kbHook, _mouseHook;
    private readonly HashSet<string> _swallowed = new(); // ids whose key-up must also be swallowed

    private uint _cachedPid;
    private string _cachedName = "";
    private bool _ctrlHeld;

    /// <summary>Return true if this slot (1-9) has a stratagem assigned and should be handled.</summary>
    public Func<int, bool> ShouldHandle { get; set; } = _ => false;
    public Action<int>? SlotTriggered { get; set; }
    /// <summary>Ctrl+C while the game is focused; C is swallowed.</summary>
    public Action? DetectRequested { get; set; }
    /// <summary>Ctrl+V while the game is focused; V is swallowed.</summary>
    public Action? OverlayToggleRequested { get; set; }
    /// <summary>Physical Ctrl pressed / released while the game is focused (for the number labels).</summary>
    public Action? CtrlDown { get; set; }
    public Action? CtrlUp { get; set; }

    /// <summary>One-touch bindings: id -> action. Swallowed while the game is focused.</summary>
    public IReadOnlyDictionary<string, Action> Bindings { get; set; } = new Dictionary<string, Action>();

    /// <summary>Push-to-talk binding (not swallowed, so it can share a key with in-game chat).</summary>
    public string? PttBinding { get; set; }
    public volatile bool PttHeld;
    public DateTime PttReleasedUtc { get; private set; } = DateTime.MinValue;

    /// <summary>While set, the next key / extra mouse button press is passed here (and swallowed).</summary>
    public Action<string>? Capture { get; set; }

    public volatile bool Enabled = true;

    public const uint DetectVk = 0x43;        // C
    public const uint OverlayToggleVk = 0x56; // V
    public static readonly string[] GameProcessNames = { "helldivers2" };

    public KeyboardHook()
    {
        _kbProc = KeyboardCallback;
        _mouseProc = MouseCallback;
        using var module = Process.GetCurrentProcess().MainModule!;
        var hMod = GetModuleHandle(module.ModuleName);
        _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, hMod, 0);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);
        if (_kbHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to install input hooks (error {Marshal.GetLastWin32Error()}).");
    }

    // ---------------- Keyboard ----------------

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;
            bool down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            bool up = msg is WM_KEYUP or WM_SYSKEYUP;
            bool injected = (info.flags & LLKHF_INJECTED) != 0;

            if (!injected && (down || up) && HandleButton(StratagemHelper.Bindings.Key(info.vkCode), info.vkCode, down))
                return (IntPtr)1;
        }
        return CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    // ---------------- Mouse ----------------

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            string? id = null;
            bool down = false;
            if (msg is WM_MBUTTONDOWN or WM_MBUTTONUP or WM_XBUTTONDOWN or WM_XBUTTONUP)
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if ((info.flags & LLMHF_INJECTED) == 0)
                {
                    if (msg is WM_MBUTTONDOWN or WM_MBUTTONUP)
                        id = "M3";
                    else
                    {
                        int button = (int)(info.mouseData >> 16);
                        id = button == 1 ? "M4" : button == 2 ? "M5" : null;
                    }
                    down = msg is WM_MBUTTONDOWN or WM_XBUTTONDOWN;
                }
            }
            if (id != null && HandleButton(id, 0, down))
                return (IntPtr)1;
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    // ---------------- Shared ----------------

    /// <summary>Returns true to swallow the event.</summary>
    private bool HandleButton(string id, uint vk, bool down)
    {
        // Binding capture for the settings window: works anywhere, swallows the press.
        if (Capture != null && down)
        {
            var cap = Capture;
            Capture = null;
            _swallowed.Add(id);
            cap(id);
            return true;
        }

        if (!down)
        {
            if (vk is 0x11 or 0xA2 or 0xA3 && _ctrlHeld)
            {
                _ctrlHeld = false;
                CtrlUp?.Invoke();
            }
            if (id == PttBinding && PttHeld)
            {
                PttHeld = false;
                PttReleasedUtc = DateTime.UtcNow;
            }
            return _swallowed.Remove(id);
        }

        // Key auto-repeat of something we're already swallowing.
        if (_swallowed.Contains(id)) return true;

        if (vk is 0x11 or 0xA2 or 0xA3)
        {
            if (!_ctrlHeld)
            {
                _ctrlHeld = true;
                if (Enabled && GameIsFocused()) CtrlDown?.Invoke();
            }
            return false;
        }

        if (id == PttBinding) PttHeld = true; // falls through: PTT is never swallowed

        // One-touch bindings.
        if (Bindings.TryGetValue(id, out var action) && Enabled && GameIsFocused())
        {
            _swallowed.Add(id);
            action();
            return true;
        }

        bool ctrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
        if (!ctrlDown || !Enabled) return false;

        // Ctrl+C: read the loadout from the stratagem menu. Ctrl+V: show/hide the loadout overlay.
        // These are copy/paste everywhere else; like every hotkey here they only act while the game is focused.
        if (vk == DetectVk && DetectRequested != null && GameIsFocused())
        {
            _swallowed.Add(id);
            DetectRequested();
            return true;
        }
        if (vk == OverlayToggleVk && OverlayToggleRequested != null && GameIsFocused())
        {
            _swallowed.Add(id);
            OverlayToggleRequested();
            return true;
        }

        // Ctrl+1..9 (top row).
        if (vk >= 0x31 && vk <= 0x39)
        {
            int slot = (int)(vk - 0x30);
            if (ShouldHandle(slot) && GameIsFocused())
            {
                _swallowed.Add(id);
                var handler = SlotTriggered;
                if (handler != null) ThreadPool.QueueUserWorkItem(_ => handler(slot));
                return true;
            }
        }
        return false;
    }

    private bool GameIsFocused()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            if (pid != _cachedPid)
            {
                using var p = Process.GetProcessById((int)pid);
                _cachedName = p.ProcessName;
                _cachedPid = pid;
            }
            return GameProcessNames.Any(n => string.Equals(n, _cachedName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Thread-safe focus check (no caching) for use off the UI thread, e.g. voice commands.</summary>
    public bool GameIsFocusedNow()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            using var p = Process.GetProcessById((int)pid);
            return GameProcessNames.Any(n => string.Equals(n, p.ProcessName, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    public void Dispose()
    {
        if (_kbHook != IntPtr.Zero) { UnhookWindowsHookEx(_kbHook); _kbHook = IntPtr.Zero; }
        if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
    }
}
