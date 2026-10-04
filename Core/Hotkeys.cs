using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace Frostbound.Core;

internal static class Controller
{
    [StructLayout(LayoutKind.Sequential)] internal struct Gamepad { public ushort Buttons; public byte LeftTrigger, RightTrigger; public short LeftX, LeftY, RightX, RightY; }
    [StructLayout(LayoutKind.Sequential)] internal struct State { public uint Packet; public Gamepad Gamepad; }
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] private static extern uint Get14(uint index, out State state);
    [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")] private static extern uint Get13(uint index, out State state);
    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")] private static extern uint Get910(uint index, out State state);
    private static int library = 14;
    public static bool Read(uint index, out State state)
    {
        state = default;
        try { return (library switch { 14 => Get14(index, out state), 13 => Get13(index, out state), _ => Get910(index, out state) }) == 0; }
        catch (DllNotFoundException) { if (library == 14) { library = 13; return Read(index, out state); } if (library == 13) { library = 9; return Read(index, out state); } return false; }
        catch (EntryPointNotFoundException) { return false; }
    }
    public static readonly Dictionary<string, uint> Buttons = new(StringComparer.OrdinalIgnoreCase) {
        ["Up"] = 0x1, ["Down"] = 0x2, ["Left"] = 0x4, ["Right"] = 0x8,
        ["Start"] = 0x10, ["Back"] = 0x20, ["LS"] = 0x40, ["RS"] = 0x80,
        ["LB"] = 0x100, ["RB"] = 0x200, ["A"] = 0x1000, ["B"] = 0x2000, ["X"] = 0x4000, ["Y"] = 0x8000,
        ["LT"] = 0x10000, ["RT"] = 0x20000
    };
    public static uint Mask(State state) => state.Gamepad.Buttons | (state.Gamepad.LeftTrigger > 160 ? 0x10000u : 0) | (state.Gamepad.RightTrigger > 160 ? 0x20000u : 0);
    public static string CurrentChord()
    {
        for (uint i = 0; i < 4; i++) if (Read(i, out var state) && Mask(state) != 0)
            return string.Join("+", Buttons.Where(pair => (Mask(state) & pair.Value) != 0).Select(pair => pair.Key));
        return "";
    }
}

public sealed class HotkeyBinding
{
    private readonly int[] keys;
    private readonly uint controllerMask;
    private readonly bool controller;
    private HotkeyBinding(int[] keys, uint mask, bool isController) { this.keys = keys; controllerMask = mask; controller = isController; }
    public static HotkeyBinding Parse(string text, bool controller)
    {
        if (string.IsNullOrWhiteSpace(text)) return new([], 0, controller);
        string input = text.Trim().Replace("Num+", "NumPlus", StringComparison.OrdinalIgnoreCase);
        var tokens = input.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (controller) {
            uint mask = 0;
            foreach (var token in tokens) { if (!Controller.Buttons.TryGetValue(token, out var value)) throw new FormatException($"未知手柄按键：{token}"); mask |= value; }
            return new([], mask, true);
        }
        var keys = new List<int>();
        foreach (var token in tokens) {
            int key = token.ToUpperInvariant() switch {
                "CTRL" or "CONTROL" => 0x11, "SHIFT" => 0x10, "ALT" => 0x12, "WIN" => 0x5B,
                "ENTER" or "RETURN" => 0x0D, "NUMPLUS" or "ADD" => 0x6B,
                "NUM-" or "SUBTRACT" => 0x6D, "NUM*" or "MULTIPLY" => 0x6A,
                "NUM/" or "DIVIDE" => 0x6F, "SPACE" => 0x20, "ESC" or "ESCAPE" => 0x1B,
                "BACKSPACE" => 0x08, "DELETE" or "DEL" => 0x2E, "INSERT" or "INS" => 0x2D,
                "PAGEUP" => 0x21, "PAGEDOWN" => 0x22, "UP" => 0x26, "DOWN" => 0x28, "LEFT" => 0x25, "RIGHT" => 0x27,
                _ => 0
            };
            if (key == 0 && token.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && int.TryParse(token[3..], out int digit) && digit is >= 0 and <= 9) key = 0x60 + digit;
            if (key == 0 && Enum.TryParse<Key>(token, true, out var parsed)) key = KeyInterop.VirtualKeyFromKey(parsed);
            if (key == 0 && token.Length == 1 && char.IsAsciiLetterOrDigit(token[0])) key = char.ToUpperInvariant(token[0]);
            if (key == 0) throw new FormatException($"未知键盘按键：{token}");
            keys.Add(key);
        }
        if (keys.All(x => x is 0x10 or 0x11 or 0x12 or 0x5B)) throw new FormatException("快捷键需要至少一个普通按键。");
        return new(keys.Distinct().ToArray(), 0, false);
    }
    public bool Down()
    {
        if (controller) {
            if (controllerMask == 0) return false;
            for (uint i = 0; i < 4; i++) if (Controller.Read(i, out var state) && (Controller.Mask(state) & controllerMask) == controllerMask) return true;
            return false;
        }
        if (keys.Length == 0 || !keys.All(key => (Native.GetAsyncKeyState(key) & 0x8000) != 0)) return false;
        foreach (int modifier in new[] { 0x10, 0x11, 0x12 }) if (!keys.Contains(modifier) && (Native.GetAsyncKeyState(modifier) & 0x8000) != 0) return false;
        return true;
    }
    public bool SameChord(HotkeyBinding other) => controller == other.controller && (controller
        ? controllerMask != 0 && controllerMask == other.controllerMask
        : keys.Length > 0 && keys.OrderBy(key => key).SequenceEqual(other.keys.OrderBy(key => key)));
    public static string FromKey(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key switch { Key.Return => "Enter", Key.Add => "Num+", Key.Subtract => "Num-", Key.Multiply => "Num*", Key.Divide => "Num/", >= Key.NumPad0 and <= Key.NumPad9 => "Num" + ((int)key - (int)Key.NumPad0), _ => key.ToString() });
        return string.Join("+", parts);
    }
}

public sealed class HotkeyService : IDisposable
{
    private readonly DispatcherTimer timer;
    private readonly GameEngine engine;
    private readonly Func<HotkeySettings> settings;
    private HotkeyBinding[] bindings = [];
    private readonly bool[] held = new bool[4];
    private DateTime lastAction;
    public bool Suspended { get; set; }
    public event Action<bool>? Triggered;
    public HotkeyService(GameEngine engine, Func<HotkeySettings> settings)
    {
        this.engine = engine; this.settings = settings;
        Reload(); timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += Tick; timer.Start();
    }
    public void Reload()
    {
        var s = settings();
        bindings = [HotkeyBinding.Parse(s.RestartKeyboard, false), HotkeyBinding.Parse(s.RestartController, true),
            HotkeyBinding.Parse(s.ResetKeyboard, false), HotkeyBinding.Parse(s.ResetController, true)];
        Array.Fill(held, true);
    }
    private void Tick(object? sender, EventArgs e)
    {
        var s = settings();
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint pid);
        bool allowed = !Suspended && s.Enabled && engine.Snapshot.CanAct && !engine.Busy && (!s.ForegroundOnly || pid == engine.Snapshot.ProcessId);
        for (int i = 0; i < bindings.Length; i++) {
            bool down = bindings[i].Down();
            if (allowed && down && !held[i] && DateTime.UtcNow - lastAction > TimeSpan.FromMilliseconds(650)) {
                lastAction = DateTime.UtcNow; Triggered?.Invoke(i < 2);
            }
            held[i] = down;
        }
    }
    public void Dispose() { timer.Stop(); timer.Tick -= Tick; }
}
