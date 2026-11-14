using System.Diagnostics;
using System.Runtime.InteropServices;
using static StratagemHelper.Native;

namespace StratagemHelper;

/// <summary>
/// Global low-level keyboard + mouse hooks. Handles Ctrl+1..9 / Ctrl+C / Ctrl+V, one-touch bindings (keys, middle and
/// side mouse buttons, pedals), push-to-talk state, binding capture for the settings UI, and physical Ctrl
/// up/down events.
/// <para>
/// Windows makes every keystroke and mouse event in the whole system wait for a low-level hook to return, so the
/// hooks live on their own high-priority thread that does nothing else: whatever the UI thread is busy with (the
/// settings window, saving, applying a scan) can never delay input to the game. The mouse hook is only installed
/// while something is bound to a mouse button, so mouse movement normally doesn't pass through this app at all.
/// </para>
/// <para>
/// Threading: <see cref="Capture"/>, <see cref="CtrlDown"/>, <see cref="CtrlUp"/> and
/// <see cref="OverlayToggleRequested"/> are posted to the UI thread. <see cref="ShouldHandle"/>,
/// <see cref="DetectRequested"/> and the <see cref="Bindings"/> actions run on the hook thread and must return
/// immediately (start a task for real work). <see cref="SlotTriggered"/> runs on the thread pool.
/// </para>
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    private readonly LowLevelKeyboardProc _kbProc;   // keep references so the GC doesn't collect them
    private readonly LowLevelKeyboardProc _mouseProc;
    private IntPtr _kbHook, _mouseHook;              // hook thread only
    private readonly HashSet<string> _swallowed = new(); // hook thread only: ids whose key-up must also be swallowed
    private bool _ctrlHeld;                          // hook thread only

    private readonly SynchronizationContext _ui;
    private readonly Thread _thread;
    private uint _threadId;
    private int _startError;
    private const uint WM_REFRESH_MOUSE = WM_APP + 1;

    private static readonly string[] KeyIds = Enumerable.Range(0, 256).Select(vk => StratagemHelper.Bindings.Key((uint)vk)).ToArray();

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

    private volatile IReadOnlyDictionary<string, Action> _bindings = new Dictionary<string, Action>();
    /// <summary>One-touch bindings: id -> action. Swallowed while the game is focused.</summary>
    public IReadOnlyDictionary<string, Action> Bindings
    {
        get => _bindings;
        set { _bindings = value; RequestMouseRefresh(); }
    }

    private volatile string? _pttBinding;
    /// <summary>Push-to-talk binding (not swallowed, so it can share a key with in-game chat).</summary>
    public string? PttBinding
    {
        get => _pttBinding;
        set { _pttBinding = value; RequestMouseRefresh(); }
    }
    public volatile bool PttHeld;
    private long _pttReleasedTicks = DateTime.MinValue.Ticks;
    public DateTime PttReleasedUtc => new(Interlocked.Read(ref _pttReleasedTicks), DateTimeKind.Utc);

    private Action<string>? _capture;
    /// <summary>While set, the next key / extra mouse button press is passed here (and swallowed).</summary>
    public Action<string>? Capture
    {
        get => Volatile.Read(ref _capture);
        set { Volatile.Write(ref _capture, value); RequestMouseRefresh(); }
    }

    public volatile bool Enabled = true;

    public const uint DetectVk = 0x43;        // C
    public const uint OverlayToggleVk = 0x56; // V

    public KeyboardHook()
    {
        // Created on the UI thread, so this is the WinForms context the UI callbacks are posted to.
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _kbProc = KeyboardCallback;
        _mouseProc = MouseCallback;

        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Input hooks", Priority = ThreadPriority.Highest };
        _thread.Start();
        ready.Wait();
        if (_startError != 0)
            throw new InvalidOperationException($"Failed to install input hooks (error {_startError}).");
    }

    private void Run(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0); // creates this thread's message queue so posts to it can't be lost

        _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, ModuleHandle(), 0);
        if (_kbHook == IntPtr.Zero)
        {
            _startError = Marshal.GetLastWin32Error();
            ready.Set();
            return;
        }
        RefreshMouseHook();
        ready.Set();

        // Hook callbacks are delivered while this thread waits in GetMessage.
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            if (msg.message == WM_REFRESH_MOUSE) RefreshMouseHook();

        if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        UnhookWindowsHookEx(_kbHook);
        _kbHook = IntPtr.Zero;
    }

    private static IntPtr ModuleHandle()
    {
        using var module = Process.GetCurrentProcess().MainModule!;
        return GetModuleHandle(module.ModuleName);
    }

    private void Post(Action a) => _ui.Post(static s => ((Action)s!)(), a);

    // ---------------- Keyboard ----------------

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            bool down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            bool up = msg is WM_KEYUP or WM_SYSKEYUP;
            if (down || up)
            {
                // KBDLLHOOKSTRUCT: vkCode at 0, flags at 8. Read in place: this runs for every key in the system.
                uint vk = (uint)Marshal.ReadInt32(lParam, 0);
                uint flags = (uint)Marshal.ReadInt32(lParam, 8);
                bool injected = (flags & LLKHF_INJECTED) != 0;
                string id = vk < KeyIds.Length ? KeyIds[vk] : StratagemHelper.Bindings.Key(vk);
                if (!injected && HandleButton(id, vk, down))
                    return (IntPtr)1;
            }
        }
        return CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    // ---------------- Mouse ----------------

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg is WM_MBUTTONDOWN or WM_MBUTTONUP or WM_XBUTTONDOWN or WM_XBUTTONUP)
            {
                string? id = null;
                bool down = false;
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
                if (id != null && HandleButton(id, 0, down))
                    return (IntPtr)1;
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private static bool IsMouse(string? id) => id is "M3" or "M4" or "M5";

    /// <summary>Asks the hook thread to install or remove the mouse hook to match what's bound.</summary>
    private void RequestMouseRefresh()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_REFRESH_MOUSE, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Hook thread only.</summary>
    private void RefreshMouseHook()
    {
        bool need = Capture != null
                    || IsMouse(PttBinding)
                    || Bindings.Keys.Any(IsMouse)
                    || _swallowed.Any(IsMouse); // keep it until a swallowed button's release has been swallowed too
        if (need && _mouseHook == IntPtr.Zero)
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, ModuleHandle(), 0);
        else if (!need && _mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    // ---------------- Shared ----------------

    /// <summary>Returns true to swallow the event. Hook thread only.</summary>
    private bool HandleButton(string id, uint vk, bool down)
    {
        // Binding capture for the settings window: works anywhere, swallows the press.
        if (down && Capture != null && Interlocked.Exchange(ref _capture, null) is { } cap)
        {
            _swallowed.Add(id);
            Post(() => cap(id));
            return true;
        }

        if (!down)
        {
            if (vk is 0x11 or 0xA2 or 0xA3 && _ctrlHeld)
            {
                _ctrlHeld = false;
                if (CtrlUp is { } ctrlUp) Post(ctrlUp);
            }
            if (id == PttBinding && PttHeld)
            {
                PttHeld = false;
                Interlocked.Exchange(ref _pttReleasedTicks, DateTime.UtcNow.Ticks);
            }
            bool swallow = _swallowed.Remove(id);
            if (swallow && IsMouse(id)) RequestMouseRefresh(); // the mouse hook may no longer be needed
            return swallow;
        }

        // Key auto-repeat of something we're already swallowing.
        if (_swallowed.Contains(id)) return true;

        if (vk is 0x11 or 0xA2 or 0xA3)
        {
            if (!_ctrlHeld)
            {
                _ctrlHeld = true;
                if (Enabled && GameWindow.IsForeground() && CtrlDown is { } ctrlDown) Post(ctrlDown);
            }
            return false;
        }

        if (id == PttBinding) PttHeld = true; // falls through: PTT is never swallowed

        // One-touch bindings.
        if (Bindings.TryGetValue(id, out var action) && Enabled && GameWindow.IsForeground())
        {
            _swallowed.Add(id);
            action();
            return true;
        }

        if (vk == 0 || !Enabled) return false;
        if ((GetAsyncKeyState(VK_CONTROL) & 0x8000) == 0) return false;

        // Ctrl+C: read the loadout from the stratagem menu. Ctrl+V: show/hide the loadout overlay.
        // These are copy/paste everywhere else; like every hotkey here they only act while the game is focused.
        if (vk == DetectVk && DetectRequested != null && GameWindow.IsForeground())
        {
            _swallowed.Add(id);
            DetectRequested();
            return true;
        }
        if (vk == OverlayToggleVk && OverlayToggleRequested is { } toggle && GameWindow.IsForeground())
        {
            _swallowed.Add(id);
            Post(toggle);
            return true;
        }

        // Ctrl+1..9 (top row).
        if (vk >= 0x31 && vk <= 0x39)
        {
            int slot = (int)(vk - 0x30);
            if (ShouldHandle(slot) && GameWindow.IsForeground())
            {
                _swallowed.Add(id);
                var handler = SlotTriggered;
                if (handler != null) ThreadPool.QueueUserWorkItem(_ => handler(slot));
                return true;
            }
        }
        return false;
    }

    /// <summary>Thread-safe focus check for use off the hook thread, e.g. voice commands.</summary>
    public bool GameIsFocusedNow() => GameWindow.IsForeground();

    public void Dispose()
    {
        if (_threadId == 0 || !_thread.IsAlive) return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
    }
}

/// <summary>
/// "Is this window Helldivers 2?" Asked on every hotkey and every overlay tick, so the answer is cached per process
/// id: looking a process up by id is far slower than the rest of the check, and the foreground process rarely changes.
/// Safe to call from any thread.
/// </summary>
internal static class GameWindow
{
    public static readonly string[] ProcessNames = { "helldivers2" };

    private sealed record Entry(uint Pid, bool IsGame);
    private static volatile Entry _last = new(0, false);

    public static bool IsForeground() => Is(GetForegroundWindow());

    public static bool Is(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return false;
        var last = _last;
        if (last.Pid == pid) return last.IsGame;

        bool isGame;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            isGame = IsGameName(p.ProcessName);
        }
        catch { isGame = false; }
        _last = new Entry(pid, isGame);
        return isGame;
    }

    public static bool IsGameName(string processName) =>
        ProcessNames.Any(n => string.Equals(n, processName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Drops the cached answer (the game closed, so its process id may be reused).</summary>
    public static void Forget() => _last = new Entry(0, false);
}
