using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Frostbound.Core;

internal static class Native
{
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ReadProcessMemory(SafeProcessHandle process, nint address, byte[] buffer, nuint size, out nuint read);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WriteProcessMemory(SafeProcessHandle process, nint address, byte[] buffer, nuint size, out nuint written);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool VirtualProtectEx(SafeProcessHandle process, nint address, nuint size, uint protection, out uint old);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool FlushInstructionCache(SafeProcessHandle process, nint address, nuint size);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ShowWindow(nint window, int command);
}

public sealed class ProcessMemory : IDisposable
{
    private readonly SafeProcessHandle handle;
    private readonly object sync = new();
    public Process Process { get; }
    public long BaseAddress { get; }
    public string ExecutablePath { get; }
    public bool IsAlive { get { try { return !Process.HasExited && !handle.IsClosed; } catch { return false; } } }
    public ProcessMemory(Process process)
    {
        Process = process;
        handle = Native.OpenProcess(0x0010 | 0x0020 | 0x0008 | 0x1000, false, process.Id);
        if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        try {
            var module = process.MainModule ?? throw new IOException("无法读取游戏模块。 / Game module is unavailable.");
            BaseAddress = module.BaseAddress.ToInt64(); ExecutablePath = module.FileName;
        } catch { handle.Dispose(); throw; }
    }
    public byte[] Read(long address, int size)
    {
        if (address < 0x10000 || address > 0x00007FFFFFFFFFFF - size || size is < 1 or > 0x10000000)
            throw new IOException($"无效内存地址 0x{address:X}。 / Invalid address.");
        var bytes = new byte[size];
        lock (sync) {
            if (!Native.ReadProcessMemory(handle, (nint)address, bytes, (nuint)size, out var read) || read != (nuint)size)
                throw new IOException($"内存读取失败 0x{address:X} ({Marshal.GetLastWin32Error()})。 / Read failed.");
        }
        return bytes;
    }
    public void Write(long address, byte[] bytes)
    {
        if (address < 0x10000 || address > 0x00007FFFFFFFFFFF - bytes.Length) throw new IOException("无效写入地址。 / Invalid write address.");
        lock (sync) {
            if (!Native.WriteProcessMemory(handle, (nint)address, bytes, (nuint)bytes.Length, out var written) || written != (nuint)bytes.Length)
                throw new IOException($"内存写入失败 0x{address:X} ({Marshal.GetLastWin32Error()})。 / Write failed.");
        }
    }
    public long Pointer(long address) { long p = BitConverter.ToInt64(Read(address, 8)); if (p < 0x10000 || p > 0x00007FFFFFFFFFFF) throw new IOException($"对象尚未就绪 0x{address:X}。 / Object is not ready."); return p; }
    public long Follow(long root, params int[] offsets) { long p = Pointer(root); foreach (int offset in offsets) p = Pointer(p + offset); return p; }
    public int Int32(long address) => BitConverter.ToInt32(Read(address, 4));
    public uint UInt32(long address) => BitConverter.ToUInt32(Read(address, 4));
    public float Float(long address) => BitConverter.ToSingle(Read(address, 4));
    public byte Byte(long address) => Read(address, 1)[0];
    public void Int32(long address, int value) => Write(address, BitConverter.GetBytes(value));
    public void Pointer(long address, long value) => Write(address, BitConverter.GetBytes(value));
    public void Float(long address, float value) => Write(address, BitConverter.GetBytes(value));
    public void Byte(long address, byte value) => Write(address, [value]);
    public string Utf8(long address, int maximum)
    {
        byte[] data = Read(address, maximum); int end = Array.IndexOf(data, (byte)0);
        return Encoding.UTF8.GetString(data, 0, end < 0 ? data.Length : end).Trim();
    }
    public void WriteCode(long address, byte[] data)
    {
        lock (sync) {
            if (!Native.VirtualProtectEx(handle, (nint)address, (nuint)data.Length, 0x40, out uint protection)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { Write(address, data); if (!Native.FlushInstructionCache(handle, (nint)address, (nuint)data.Length)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
            finally { if (!Native.VirtualProtectEx(handle, (nint)address, (nuint)data.Length, protection, out _)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
        }
    }
    public void WriteProtectedData(long address, byte[] data)
    {
        if (data.Length is < 1 or > 0x1000 || address < 0x10000 || address > 0x00007FFFFFFFFFFF - data.Length)
            throw new IOException("无效参数写入地址。 / Invalid parameter address.");
        lock (sync) {
            // Only image constants use this path. Normal game-object writes retain their original checks.
            if (!Native.VirtualProtectEx(handle, (nint)address, (nuint)data.Length, 0x04, out uint protection))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try { Write(address, data); }
            finally {
                if (!Native.VirtualProtectEx(handle, (nint)address, (nuint)data.Length, protection, out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
    }
    public void Dispose() { lock (sync) { handle.Dispose(); Process.Dispose(); } }
}
