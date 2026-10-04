using System;
using System.Windows;
using Frostbound.Core;
using Frostbound.UI;

namespace Frostbound;

public partial class MainWindow
{
    internal event Action? HotkeysChanged;

    internal string HotkeyName(int index) => index switch {
        0 => T("重启 · 键盘", "Restart · keyboard", "再開 · キーボード"),
        1 => T("重启 · 手柄", "Restart · controller", "再開 · コントローラー"),
        2 => T("重置 · 键盘", "Reset · keyboard", "リセット · キーボード"),
        _ => T("重置 · 手柄", "Reset · controller", "リセット · コントローラー")
    };

    internal HotkeySettings CopyHotkeys()
    {
        var value = Runtime.Settings.Hotkeys;
        return new() { RestartKeyboard = value.RestartKeyboard, RestartController = value.RestartController, ResetKeyboard = value.ResetKeyboard,
            ResetController = value.ResetController, Enabled = value.Enabled, ForegroundOnly = value.ForegroundOnly };
    }

    internal void SaveHotkeys(HotkeySettings value)
    {
        var restartKey = HotkeyBinding.Parse(value.RestartKeyboard, false); var restartPad = HotkeyBinding.Parse(value.RestartController, true);
        var resetKey = HotkeyBinding.Parse(value.ResetKeyboard, false); var resetPad = HotkeyBinding.Parse(value.ResetController, true);
        if (restartKey.SameChord(resetKey) || restartPad.SameChord(resetPad))
            throw new FormatException(T("重启与重置不能使用相同的快捷键。", "Restart and reset need different bindings.", "再開とリセットは別のキーにしてください。"));
        var previous = Runtime.Settings.Hotkeys; Runtime.Settings.Hotkeys = value;
        try { if (!App.IsRendering) Runtime.Store.Save(Runtime.Settings); }
        catch { Runtime.Settings.Hotkeys = previous; throw; }
        Runtime.Hotkeys.Reload(); pages.Remove(3); HotkeysChanged?.Invoke();
        ShowToast(T("快捷键已保存。", "Shortcuts saved.", "保存しました。"));
    }

    internal string? CaptureHotkey(bool controller, string title)
    {
        bool wasSuspended = Runtime.Hotkeys.Suspended; Runtime.Hotkeys.Suspended = true;
        try { var dialog = new KeyCaptureDialog(this, controller, title); return dialog.ShowDialog() == true ? dialog.Binding : null; }
        finally { Runtime.Hotkeys.Suspended = wasSuspended; }
    }

    internal void RecordHotkey(int index)
    {
        var binding = CaptureHotkey(index % 2 == 1, HotkeyName(index)); if (binding == null) return;
        try {
            var settings = CopyHotkeys();
            switch (index) { case 0: settings.RestartKeyboard = binding; break; case 1: settings.RestartController = binding; break; case 2: settings.ResetKeyboard = binding; break; default: settings.ResetController = binding; break; }
            SaveHotkeys(settings);
        } catch (Exception e) { ShowToast(e.Message); }
    }

    internal void OpenHotkeySettings()
    {
        bool wasSuspended = Runtime.Hotkeys.Suspended; Runtime.Hotkeys.Suspended = true;
        try { new HotkeySettingsDialog(this).ShowDialog(); }
        finally { Runtime.Hotkeys.Suspended = wasSuspended; }
    }
}
