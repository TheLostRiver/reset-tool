using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Frostbound.Core;

public sealed partial class GameEngine
{
    private const int ItemCount = 40, ItemSize = 0x10, ActiveItemsOffset = 0x38080;
    private const int ItemBarSize = 0x140, PresetItemBarOffset = 0x278, ActiveItemBarOffset = 0xEDF38;
    private const int PaletteNamesSize = 0x80, PaletteActionCount = 32, PaletteActionSize = 0x18;
    private const int PresetPaletteNamesOffset = 0x3B8, PresetPaletteActionsOffset = 0x438;
    private const int ControllerNamesOffset = 0xF45B8, ControllerActionsOffset = 0xF4638;
    private const int KeyboardNamesOffset = 0xF493C, KeyboardActionsOffset = 0xF49C0;

    private bool ItemRecordExists(long save, int recordNumber)
    {
        int index = recordNumber - 1;
        return (M.UInt32(save + 0x38070 + index / 32 * 4) & (1u << (index % 32))) != 0;
    }

    // The first eight bytes of each live item are its object header, followed by ID and quantity.
    private static byte[] ItemValues(byte[] objects)
    {
        var values = new byte[ItemCount * 8];
        for (int index = 0; index < ItemCount; index++) Buffer.BlockCopy(objects, index * ItemSize + 8, values, index * 8, 8);
        return values;
    }

    private static byte[] PaletteValues(byte[] objects)
    {
        var values = new byte[PaletteActionCount * 16];
        for (int index = 0; index < PaletteActionCount; index++) {
            // Copy action, parameter, two flags and extra data; exclude object headers and padding.
            Buffer.BlockCopy(objects, index * PaletteActionSize + 8, values, index * 16, 10);
            Buffer.BlockCopy(objects, index * PaletteActionSize + 0x14, values, index * 16 + 12, 4);
        }
        return values;
    }

    private PaletteSnapshot ReadPresetPalette(long record) => new(
        M.Read(record + PresetPaletteNamesOffset, PaletteNamesSize),
        PaletteValues(M.Read(record + PresetPaletteActionsOffset, PaletteActionCount * PaletteActionSize)));

    private PaletteSnapshot ReadActivePalette(long save, bool keyboard) => new(
        M.Read(save + (keyboard ? KeyboardNamesOffset : ControllerNamesOffset), PaletteNamesSize),
        PaletteValues(M.Read(save + (keyboard ? KeyboardActionsOffset : ControllerActionsOffset), PaletteActionCount * PaletteActionSize)));

    private bool AddPaletteWrites(MemoryBatch batch, long save, PaletteSnapshot desired, bool keyboard)
    {
        long names = save + (keyboard ? KeyboardNamesOffset : ControllerNamesOffset);
        long actions = save + (keyboard ? KeyboardActionsOffset : ControllerActionsOffset);
        bool changed = false;
        if (!M.Read(names, PaletteNamesSize).SequenceEqual(desired.Names)) { batch.Add(names, desired.Names); changed = true; }
        var current = M.Read(actions, PaletteActionCount * PaletteActionSize);
        if (!PaletteValues(current).SequenceEqual(desired.Actions)) {
            for (int index = 0; index < PaletteActionCount; index++) {
                Buffer.BlockCopy(desired.Actions, index * 16, current, index * PaletteActionSize + 8, 10);
                Buffer.BlockCopy(desired.Actions, index * 16 + 12, current, index * PaletteActionSize + 0x14, 4);
            }
            batch.Add(actions, current); changed = true;
        }
        return changed;
    }

    private bool AddItemPresetWrites(MemoryBatch batch, long save, ItemPresetSnapshot preset)
    {
        bool changed = false;
        var current = M.Read(save + ActiveItemsOffset, ItemCount * ItemSize);
        if (!ItemValues(current).SequenceEqual(preset.Inventory)) {
            for (int index = 0; index < ItemCount; index++) Buffer.BlockCopy(preset.Inventory, index * 8, current, index * ItemSize + 8, 8);
            batch.Add(save + ActiveItemsOffset, current); changed = true;
        }
        // The game's refresh resolves equipment-dependent IDs and rebuilds the bar's lookup tables.
        if (!preset.BarRefreshed && !M.Read(save + ActiveItemBarOffset, ItemBarSize).SequenceEqual(preset.ItemBar)) {
            batch.Add(save + ActiveItemBarOffset, preset.ItemBar); changed = true;
        }
        changed |= AddPaletteWrites(batch, save, preset.Controller, false);
        changed |= AddPaletteWrites(batch, save, preset.Keyboard, true);
        return changed;
    }

    private long Integer64(long address) => BitConverter.ToInt64(M.Read(address, 8));

    private async Task RefreshItemBarAsync(CancellationToken token)
    {
        long root = M.Pointer(G(UiRoot));
        long entry = root + 0x4F60;
        if (M.Pointer(G(0x33F96F0) + 0x10) != G(0x1AD5040))
            throw new InvalidOperationException("游戏道具栏刷新函数与当前适配不一致，操作已停止。 / Item refresh profile mismatch.");
        await WaitAsync(() => Integer64(root + 0x8F60) == 0 && Integer64(entry + 0x38) == 0,
            token, 10, "游戏界面请求尚未完成，请关闭菜单后重试。 / Game UI requests are pending.", 25);
        token.ThrowIfCancellationRequested();
        if (M.Pointer(G(UiRoot)) != root || M.Int32(M.Pointer(G(SaveRoot)) + 0xA0) != operationSaveSlot)
            throw new InvalidOperationException("游戏对象或猎人存档已变化，操作已停止。 / Game context changed.");

        // Queue a game-owned refresh on its UI update thread. It normalizes the item bar and updates
        // its HUD cache; it does not load a preset, modify saved presets or transfer storage items.
        var request = new byte[0x80];
        Buffer.BlockCopy(BitConverter.GetBytes(G(0x33F96F0)), 0, request, 0, 8);
        Buffer.BlockCopy(BitConverter.GetBytes(root), 0, request, 8, 8);
        Buffer.BlockCopy(BitConverter.GetBytes(entry), 0, request, 0x38, 8);
        var batch = new MemoryBatch(M);
        batch.Add(entry, request);
        batch.Byte(root + 0x15D70, 1);
        // Publish the initialized entry last; the game owns it until it is consumed.
        batch.Add(root + 0x8F60, BitConverter.GetBytes(1L));
        batch.Commit();
        await WaitAsync(() => Integer64(root + 0x8F60) == 0 && Integer64(entry + 0x38) == 0 && M.Byte(root + 0x15D70) == 0,
            token, 10, "游戏未完成道具栏刷新，请切回游戏并关闭菜单后重试。 / Game did not complete item refresh.", 25);
        Log("游戏已完成道具栏及其查询索引的刷新。");
    }

    private sealed record PaletteSnapshot(byte[] Names, byte[] Actions);
    private sealed record ItemPresetSnapshot(int Slot, string Name, byte[] Inventory, byte[] ItemBar,
        PaletteSnapshot Controller, PaletteSnapshot Keyboard, bool LinkRadial)
    {
        public bool BarRefreshed { get; set; }
    }
}
