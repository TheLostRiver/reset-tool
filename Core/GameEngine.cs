using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Frostbound.Core;

public sealed class GameEngine : IDisposable
{
    // This profile is bound to the exact Windows x64 executable, including ASLR.
    public const string SupportedHash = "C2EBBBD2C49F216D484E31A5219BED419EB1E5E7D206D02CBA040A3AB79D90EA";
    public const string ProfileName = "Steam x64 · C2EBBBD2";
    private const int QuestRoot = 0x500ED30, UiRoot = 0x51C4640, SaveRoot = 0x5013950;
    private const int PlayerRoot = 0x50EC7E8, LocalRoot = 0x50EC750, FoodRoot = 0x500ECA0;
    private ProcessMemory? memory;
    private long steamBase;
    private int rejectedPid;
    private readonly SemaphoreSlim operation = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<(long Address, byte[] Original, byte[] Replacement)> temporaryChanges = [];
    private FoodPreset? pendingFood;
    private int operationSaveSlot = -1;
    private long chatObject;
    private string previousChat = "";
    public GameSnapshot Snapshot { get; private set; } = new(ConnectionState.Waiting);
    public bool Busy { get; private set; }
    public event Action<GameSnapshot>? SnapshotChanged;
    public event Action<bool>? BusyChanged;
    public event Action<string, string>? Logged;
    private ProcessMemory M => memory ?? throw new InvalidOperationException("游戏尚未连接。 / Game is not connected.");
    private long G(int offset) => M.BaseAddress + offset;
    private void Log(string message, string level = "INFO") => Logged?.Invoke(level, message);
    private void Publish(GameSnapshot snapshot) { Snapshot = snapshot; SnapshotChanged?.Invoke(snapshot); }

    public async Task PollAsync(bool retry = false)
    {
        if (Busy) return;
        await operation.WaitAsync(lifetime.Token);
        try {
            if (memory != null && !memory.IsAlive) { memory.Dispose(); memory = null; steamBase = 0; pendingFood = null; chatObject = 0; previousChat = ""; temporaryChanges.Clear(); }
            if (memory == null) {
                Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint foregroundPid);
                var processes = Process.GetProcessesByName("MonsterHunterWorld").OrderByDescending(p => {
                    try { return (p.Id == foregroundPid ? 4 : 0) + (p.MainWindowHandle != 0 ? 2 : 0); }
                    catch (Exception e) when (e is Win32Exception or InvalidOperationException) { return 0; }
                }).ToArray();
                var process = processes.FirstOrDefault();
                foreach (var extra in processes.Skip(1)) extra.Dispose();
                if (process == null) { rejectedPid = 0; Publish(new(ConnectionState.Waiting)); return; }
                if (process.Id == rejectedPid && !retry) { process.Dispose(); return; }
                Publish(new(ConnectionState.Reading, process.Id, Detail: "正在识别游戏版本… / Identifying the game…"));
                ProcessMemory? candidate = null;
                int pid = process.Id;
                try {
                    candidate = new(process);
                    using var stream = File.OpenRead(candidate.ExecutablePath);
                    var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, lifetime.Token));
                    if (hash != SupportedHash) {
                        rejectedPid = process.Id;
                        Publish(new(ConnectionState.Unsupported, process.Id, candidate.BaseAddress,
                            "游戏文件版本与当前适配不一致，操作已禁用。 / Unsupported executable.", Build: hash[..12]));
                        Log($"版本校验未通过：SHA256 {hash}", "WARN"); candidate.Dispose(); return;
                    }
                    memory = candidate;
                    var steam = process.Modules.Cast<ProcessModule>().FirstOrDefault(x => x.ModuleName.Equals("steam_api64.dll", StringComparison.OrdinalIgnoreCase));
                    steamBase = steam?.BaseAddress.ToInt64() ?? 0;
                    rejectedPid = 0; Log($"已连接游戏 PID {process.Id}，版本指纹 {hash[..12]}。");
                } catch (Win32Exception e) {
                    rejectedPid = pid; candidate?.Dispose(); process.Dispose();
                    Publish(new(ConnectionState.AccessDenied, pid, Detail: "无法访问游戏进程。请保持工具与游戏的权限级别一致。 / Process access denied."));
                    Log(e.Message, "ERROR"); return;
                } catch { candidate?.Dispose(); process.Dispose(); throw; }
            }
            ReadState();
        } catch (IOException e) {
            if (memory?.IsAlive == true) Publish(new(ConnectionState.Reading, memory.Process.Id, memory.BaseAddress,
                "等待游戏载入猎人存档。 / Waiting for a hunter save.", Build: ProfileName));
            else { Publish(new(ConnectionState.Waiting)); Log(e.Message, "WARN"); }
        } finally { operation.Release(); }
    }

    private void ReadState()
    {
        long quest = M.Pointer(G(QuestRoot));
        long ui = M.Follow(G(UiRoot), 0x278);
        long load = M.Pointer(ui + 0x20);
        long save = M.Pointer(G(SaveRoot));
        long local = M.Follow(G(LocalRoot), 0x108);
        int slot = M.Int32(save + 0xA0);
        int phase = M.Int32(quest + 0x38);
        if (slot is < 0 or > 2 || phase is < 0 or > 32) throw new IOException("游戏对象状态尚未稳定。");
        int currentQuest = M.Int32(M.Pointer(ui + 0x3C8) + 0x2938);
        uint account = steamBase > 0 ? M.UInt32(steamBase + 0x44878) : 0;
        int localSlot = M.Int32(local + 0x434);
        if (localSlot is >= 0 and <= 2) slot = localSlot;
        bool loading = M.Int32(load + 0x1D04) != 0;
        bool idle = phase switch { 1 => M.Int32(local + 0x1138) != 0, 2 => M.Int32(local + 0xFE0) != 0, _ => true };
        Publish(new(ConnectionState.Ready, M.Process.Id, M.BaseAddress,
            idle ? "" : "请关闭游戏菜单或等待角色就绪。 / Close game menus and wait for the hunter.", phase,
            loading, currentQuest, slot, account, Build: ProfileName, IsActionable: idle));
    }

    public async Task RestartAsync(int selectedQuestId, RestartMode mode, bool wingdrake, bool fastFade)
    {
        await ExecuteAsync("任务重启", async token => {
            EnsureReady();
            long ui = M.Follow(G(UiRoot), 0x278);
            long accept = M.Pointer(ui + 0x3C8);
            int questId = selectedQuestId != 0 ? selectedQuestId : M.Int32(accept + 0x2938);
            if (questId is < 101 or > 67809) throw new InvalidOperationException("请先选择任务，或在游戏中正常受理一次任务。 / Select or accept a quest first.");
            int phase = M.Int32(M.Pointer(G(QuestRoot)) + 0x38);
            if (phase is not (1 or 2 or 4 or 5 or 13)) throw new InvalidOperationException("当前阶段无法重启任务。 / Cannot restart in the current state.");
            if (mode == RestartMode.AcceptOnly && phase is not (1 or 13)) throw new InvalidOperationException("仅受理模式需要先回到据点或探索。 / Accept-only requires a base or expedition.");
            using var fade = fastFade ? ChangeFade() : null;
            bool quick = mode == RestartMode.Quick && phase == 2 && CanQuickRestart();
            if (mode == RestartMode.Stable || mode == RestartMode.Quick && phase == 2 && !quick) {
                if (phase is 2 or 4 or 5) {
                    await ResetCoreAsync(token);
                    Log("等待返回据点…");
                    await WaitAsync(() => {
                        ReadState();
                        return Snapshot.QuestState is 1 or 13 && !Snapshot.Loading;
                    }, token, 60, "返回据点超时。 / Timed out returning to base.");
                    await WaitAsync(() => M.Int32(M.Follow(G(LocalRoot), 0x108) + (Snapshot.QuestState == 13 ? 0x10F0 : 0x1138)) == 3,
                        token, 20, "据点尚未准备好，请关闭菜单后重试。 / Base is not ready.");
                }
            } else if (quick) {
                ResetPlayerForQuickRestart();
            } else if (phase is 4 or 5) {
                await ResetCoreAsync(token);
                await WaitAsync(() => { ReadState(); return !Snapshot.Loading && Snapshot.QuestState is 1 or 13; }, token, 60, "等待结算完成超时。");
            }
            await AcceptCoreAsync(questId, wingdrake, mode == RestartMode.AcceptOnly, token);
        });
    }

    public Task ResetAsync(bool fastFade) => ExecuteAsync("任务重置", async token => {
        EnsureReady(); using var fade = fastFade ? ChangeFade() : null;
        await ResetCoreAsync(token);
    });

    private async Task ResetCoreAsync(CancellationToken token)
    {
        long quest = M.Pointer(G(QuestRoot));
        int phase = M.Int32(quest + 0x38);
        if (phase == 4) {
            M.Float(quest + 0x131A0, 3601f);
            Log("已跳过任务结束倒计时，等待结算。");
            await WaitAsync(() => M.Int32(M.Pointer(G(QuestRoot)) + 0x38) != 4, token, 12, "游戏没有响应结算请求。");
            quest = M.Pointer(G(QuestRoot)); phase = M.Int32(quest + 0x38);
            if (phase == 5) M.Int32(quest + 0x38, 7);
            return;
        }
        if (phase == 5) { M.Int32(quest + 0x38, 7); return; }
        if (phase != 2) throw new InvalidOperationException("没有正在进行的任务可以重置。 / No active quest to reset.");
        long window = M.Follow(G(UiRoot), 0x278, 0xA8);
        long callback = M.Pointer(window + 0x260);
        long player = M.Follow(G(PlayerRoot), 0x110);
        long health = M.Pointer(player + 0x7670);
        if (M.Float(health + 0x64) <= 0) {
            M.Int32(quest + 0x58, 6);
            M.Int32(quest + 0x38, 7);
            Log("已处理力尽状态并请求返回。");
            return;
        }
        if (M.Int32(callback + 0x124C) == 0) {
            M.Byte(window + 0x2298, 0xDA);
            await WaitAsync(() => M.Int32(M.Follow(G(UiRoot), 0x278, 0xA8, 0x260) + 0x124C) != 0,
                token, 8, "任务返回窗口没有就绪。 / Return window is not ready.");
            window = M.Follow(G(UiRoot), 0x278, 0xA8); callback = M.Pointer(window + 0x260);
        }
        var batch = new MemoryBatch(M);
        batch.Pointer(window + 0x2A20, G(0x3FC0EE0));
        batch.Pointer(callback + 0xC58, G(0x30CA9B8));
        batch.Pointer(callback + 0xC60, callback + 0x1850);
        batch.Pointer(callback + 0xC90, callback + 0xC58);
        batch.Float(callback + 0xBF0, 12f);
        batch.Int32(callback + 0x1068, 10);
        batch.Int32(window + 0x1D04, 2);
        batch.Byte(window + 0x2298, 0xC9);
        batch.Commit();
        Log("已请求按游戏流程重置任务。");
        await WaitAsync(() => { ReadState(); return Snapshot.Loading || Snapshot.QuestState != 2; }, token, 12, "游戏没有响应任务重置。");
    }

    private void ResetPlayerForQuickRestart()
    {
        long quest = M.Pointer(G(QuestRoot));
        long player = M.Follow(G(PlayerRoot), 0x110);
        long health = M.Pointer(player + 0x7670);
        if (M.Float(health + 0x64) <= 0) throw new InvalidOperationException("力尽时请使用稳定重启。 / Use stable restart while fainted.");
        long motion = M.Pointer(player + 0x7698), mantle = M.Pointer(player + 0x7D60);
        long manager = M.Pointer(G(0x5011F58));
        var batch = new MemoryBatch(M);
        batch.Int32(manager + 0x291C, 0);
        batch.Int32(player + 0x62B0, 1); batch.Int32(player + 0x62C8, 0); batch.Int32(player + 0x62C4, 1);
        batch.Int32(quest + 0x68, 0); batch.Int32(motion + 0xB4, 0); batch.Int32(motion + 0xE4, 0);
        batch.Float(mantle + 0x5EC, 5f);
        batch.Commit();
        Log("快速重启已准备；特殊战斗阶段会自动使用稳定流程。");
    }

    private bool IsJudgment()
    {
        try {
            long monster = M.Follow(G(0x50EC7F0), 0x108);
            return M.Int32(monster + 0x122C0) == 0x57 && M.Int32(monster + 0x62B8) == 0xB5;
        } catch (IOException) { return false; }
    }
    private bool CanQuickRestart()
    {
        if (IsJudgment()) { Log("当前为特殊战斗阶段，使用稳定重启。 / Special battle phase; using stable restart."); return false; }
        long player = M.Follow(G(PlayerRoot), 0x110);
        if (M.Float(M.Pointer(player + 0x7670) + 0x64) <= 0) { Log("当前处于力尽状态，使用稳定重启。 / Hunter fainted; using stable restart."); return false; }
        return true;
    }

    private async Task AcceptCoreAsync(int questId, bool wingdrake, bool acceptOnly, CancellationToken token)
    {
        long ui = M.Follow(G(UiRoot), 0x278);
        long accept = M.Pointer(ui + 0x3C8), depart = M.Pointer(ui + 0x3D0);
        var batch = new MemoryBatch(M);
        batch.Int32(accept + 0x292C, questId);
        batch.Int32(accept + 0x2938, questId);
        batch.Float(depart + 0x2AC8, 1f);
        batch.Pointer(depart + 0x2930, G(0x32263D0));
        batch.Pointer(depart + 0x2938, accept);
        batch.Int32(depart + 0x2940, -1);
        batch.Pointer(depart + 0x2968, depart + 0x2930);
        var flags = M.Read(depart + 0x2298, 4);
        flags[0] = (byte)((flags[0] & 0xF6) | 6); flags[3] = (byte)((flags[3] & 0x0F) | 0x80);
        batch.Add(depart + 0x2298, flags); batch.Commit();
        Log($"受理任务 {questId:D5}。");
        await WaitAsync(() => {
            long currentUi = M.Follow(G(UiRoot), 0x278, 0x3B8);
            return M.Int32(currentUi + 0x28F0) != 0;
        }, token, 20, "任务受理超时，请确认该任务在当前存档中可用。 / Quest acceptance timed out.");
        if (acceptOnly) { Log("任务已受理，等待手动出发。"); return; }
        depart = M.Follow(G(UiRoot), 0x278, 0x3D0);
        var readyFlags = M.Read(depart + 0x2298, 4);
        readyFlags[0] = (byte)((readyFlags[0] & 0xFA) | 0x0A); readyFlags[3] &= 0x0F;
        M.Write(depart + 0x2298, readyFlags);
        M.Float(M.Pointer(G(0x5011F58)) + 0x291C, 1f);
        long board = M.Follow(G(UiRoot), 0x278, 0x3B8);
        int mapId = M.Int32(M.Pointer(G(QuestRoot)) + 0x291B0);
        if (mapId is >= 101 and <= 109 && mapId is not (106 or 107)) M.Int32(board + 0x28F8, wingdrake ? 0 : 1);
        // The departure UI consumes this request on the game's own update thread.
        M.Int32(board + 0x28F0, 8);
        long currentQuest = M.Pointer(G(QuestRoot));
        M.Int32(currentQuest + 0x17374, 0); M.Int32(currentQuest + 0x17378, 0);
        await WaitAsync(() => { ReadState(); ApplyPendingFoodWhileLoading(); return Snapshot.QuestState == 2 && !Snapshot.Loading; },
            token, 90, "任务出发超时。 / Quest departure timed out.");
        Log($"任务 {questId:D5} 已出发。");
    }

    public Task ApplyLoadoutAsync(Loadout loadout, bool restartQuest, RestartMode mode, bool wingdrake, bool fastFade) => ExecuteAsync("应用综合套装", async token => {
        loadout.Validate(); EnsureReady(); ReadState();
        if ((restartQuest || loadout.QuestId > 0) && mode == RestartMode.AcceptOnly && Snapshot.QuestState is not (1 or 13))
            throw new InvalidOperationException("仅受理模式需要先回到据点或探索。 / Accept-only requires a base or expedition.");
        if (loadout.SaveSlot >= 0 && loadout.SaveSlot != Snapshot.SaveSlot || loadout.UserId != 0 && loadout.UserId != Snapshot.UserId)
            throw new InvalidOperationException("这个套装属于其他猎人存档，请切换存档或解除绑定。 / This preset belongs to another save.");
        long save = SaveAddress();
        int equipment = ResolveSlot(loadout.EquipmentSlot, loadout.EquipmentName, EquipmentSlots());
        int items = ResolveSlot(loadout.ItemSlot, loadout.ItemName, ItemSlots());
        bool departRequested = restartQuest || loadout.QuestId > 0;
        int targetQuest = departRequested ? (loadout.QuestId > 0 ? loadout.QuestId : M.Int32(M.Follow(G(UiRoot), 0x278, 0x3C8) + 0x2938)) : 0;
        if (departRequested && targetQuest is < 101 or > 67809)
            throw new InvalidOperationException("请先选择任务，或在游戏中正常受理一次任务。 / Select or accept a quest first.");
        using var fade = departRequested && fastFade ? ChangeFade() : null;
        if (departRequested && Snapshot.QuestState is 2 or 4 or 5) {
            if (mode == RestartMode.Quick && Snapshot.QuestState == 2 && CanQuickRestart()) ResetPlayerForQuickRestart();
            else {
                await ResetCoreAsync(token);
                await WaitAsync(() => { ReadState(); return !Snapshot.Loading && Snapshot.QuestState is 1 or 13; }, token, 60, "返回据点超时。");
            }
            // A return can restore inventory. Prepare this configuration after the return finishes.
            save = SaveAddress();
        }
        var batch = new MemoryBatch(M);
        if (equipment > 0) {
            long record = save + 0x10CCF4 + (equipment - 1) * 0x2B0L;
            if (M.Int32(record) == -1) throw new InvalidOperationException("所选装备套装为空。 / Equipment loadout is empty.");
            var active = M.Read(record, 0x24);
            batch.Add(save + 0xA8, active);
            for (int part = 0; part < 9; part++) {
                if (part == 6) continue;
                int itemId = BitConverter.ToInt32(active, part * 4);
                if (itemId == -1) continue;
                if (itemId is < 0 or > 10000) throw new IOException("装备套装内容无效。 / Invalid equipment record.");
                byte[] jewels = M.Read(record + 0x24 + part * 0xCL, 0xC);
                batch.Add(save + (part < 6 ? 0x41098 : 0xE92E8) + itemId * 0x98L, jewels);
            }
        }
        if (items > 0) {
            // The UI slot is one-based; its payload starts at slot * stride, after that slot's name.
            long record = save + items * 0x768L;
            if (M.Int32(save + items * 0x768L + 0x278) == 0) throw new InvalidOperationException("所选道具套装为空。 / Item loadout is empty.");
            batch.Add(save + 0x38088, M.Read(record, 0x270));
            if (loadout.LinkRadialMenu == true || loadout.LinkRadialMenu == null && M.Int32(save + 0x140415) == 1)
                batch.Add(save + 0xEDF38, M.Read(record + 0x278, 0x140));
        }
        long food = M.Pointer(G(FoodRoot));
        int[] foodValues = loadout.Food.ToArray();
        for (int i = 0; i < foodValues.Length; i++) batch.Int32(food + 0x19A8 + i * 4, foodValues[i]);
        batch.Commit();
        pendingFood = loadout.Food;
        Log($"综合套装「{loadout.Name}」已写入当前运行中的猎人配置。装备显示会在下一次场景载入时刷新。");
        if (departRequested) await AcceptCoreAsync(targetQuest, loadout.Wingdrake || wingdrake, mode == RestartMode.AcceptOnly, token);
    });

    public Task ApplyFoodAsync(FoodPreset food) => ExecuteAsync("应用猫饭", token => {
        token.ThrowIfCancellationRequested(); food.Validate(); EnsureReady();
        long target = M.Pointer(G(FoodRoot)); var batch = new MemoryBatch(M);
        int[] values = food.ToArray();
        for (int i = 0; i < values.Length; i++) batch.Int32(target + 0x19A8 + i * 4, values[i]);
        batch.Commit();
        Log("猫饭配置已写入游戏；生命与耐力上限会随场景载入刷新。");
        return Task.CompletedTask;
    });

    private static int ResolveSlot(int number, string name, IReadOnlyList<GameLoadoutSlot> slots)
    {
        if (number > 0) {
            if (!slots.Any(x => x.Number == number)) throw new InvalidOperationException($"套装编号 {number} 不存在或为空。 / Loadout slot is empty.");
            return number;
        }
        if (string.IsNullOrWhiteSpace(name)) return 0;
        var matches = slots.Where(x => x.Name == name).ToList();
        if (matches.Count != 1) throw new InvalidOperationException($"找不到唯一的游戏内套装「{name}」，请指定编号。 / Select a unique slot.");
        return matches[0].Number;
    }
    private long SaveAddress()
    {
        long root = M.Pointer(G(SaveRoot)); int slot = M.Int32(root + 0xA0);
        if (slot is < 0 or > 2) throw new IOException("存档索引无效。");
        return M.Pointer(root + 0xA8) + slot * 0x26CC00L;
    }
    public IReadOnlyList<GameLoadoutSlot> EquipmentSlots()
    {
        long save = SaveAddress(); var result = new List<GameLoadoutSlot>();
        for (int slot = 1; slot <= 224; slot++) {
            long record = save + 0x10CBF4 + (slot - 1) * 0x2B0L;
            if (M.Int32(record + 0x100) == -1) continue;
            string name = M.Utf8(record, 48); if (name.Length == 0) name = $"预设套装{slot}";
            result.Add(new(slot, name));
        }
        return result;
    }
    public IReadOnlyList<GameLoadoutSlot> ItemSlots()
    {
        long save = SaveAddress(); var result = new List<GameLoadoutSlot>();
        for (int slot = 1; slot <= 80; slot++) {
            if (M.Int32(save + slot * 0x768L + 0x278) == 0) continue;
            string name = M.Utf8(save + slot * 0x768L - 0x28, 48);
            if (name.Length == 0) name = $"预设套装{slot}";
            result.Add(new(slot, name));
        }
        return result;
    }
    public (IReadOnlyList<GameLoadoutSlot> Equipment, IReadOnlyList<GameLoadoutSlot> Items) ReadLoadoutSlots()
    {
        if (Busy) throw new InvalidOperationException("请等待当前操作完成。");
        operation.Wait(); try { EnsureReady(); return (EquipmentSlots(), ItemSlots()); } finally { operation.Release(); }
    }

    public string? ReadChatCommand()
    {
        if (Busy || Snapshot.State != ConnectionState.Ready) return null;
        if (!operation.Wait(0)) return null;
        try {
            long message = M.Follow(G(0x500CE70), 0x220, 0x498, 0x120);
            string text = M.Utf8(message + 0x165, 48);
            if (message == chatObject && text == previousChat) return null;
            bool first = chatObject == 0; chatObject = message; previousChat = text;
            return first || text.Length == 0 ? null : text;
        } catch (IOException) { return null; } finally { operation.Release(); }
    }

    private void ApplyPendingFoodWhileLoading()
    {
        if (pendingFood == null) return;
        try {
            if (operationSaveSlot >= 0 && M.Int32(M.Pointer(G(SaveRoot)) + 0xA0) != operationSaveSlot) return;
            long loading = M.Follow(G(UiRoot), 0x278, 0x20);
            if (M.Int32(loading + 0x1D04) == 0) return;
            long food = M.Pointer(G(FoodRoot)); int[] values = pendingFood.ToArray();
            for (int i = 0; i < values.Length; i++) M.Int32(food + 0x19A8 + i * 4, values[i]);
        } catch (IOException) { /* Objects can be replaced while a scene loads. */ }
    }
    private IDisposable ChangeFade()
    {
        var changes = new List<(long Address, byte[] Original, byte[] Replacement)>();
        RestoreFadeChanges(temporaryChanges.ToArray());
        if (temporaryChanges.Count > 0) {
            Log("淡入淡出参数仍在等待恢复，本次使用原流程速度。 / Fade restoration is pending; using the normal flow.", "WARN");
            return new RestoreScope(() => RestoreFadeChanges(temporaryChanges.ToArray()));
        }
        try {
            foreach (var offset in new[] { 0x2FC6A1C, 0x33F4F58 }) {
                var address = G(offset); var original = M.Read(address, 4); float value = BitConverter.ToSingle(original);
                if (value <= 0 || value > 5 || float.IsNaN(value)) continue;
                var replacement = BitConverter.GetBytes(.1f);
                changes.Add((address, original, replacement)); temporaryChanges.Add((address, original, replacement));
                M.WriteProtectedData(address, replacement);
            }
        } catch (Exception e) when (e is IOException or Win32Exception) {
            RestoreFadeChanges(changes);
            Log($"缩短淡入淡出未能应用，将按原速度继续任务操作：{e.Message}", "WARN");
        }
        return new RestoreScope(() => RestoreFadeChanges(changes));
    }
    private void RestoreFadeChanges(IEnumerable<(long Address, byte[] Original, byte[] Replacement)> changes)
    {
        foreach (var change in changes.Reverse().ToArray()) {
            try {
                if (memory?.IsAlive == true && M.Read(change.Address, 4).SequenceEqual(change.Replacement))
                    M.WriteProtectedData(change.Address, change.Original);
                temporaryChanges.Remove(change);
            } catch (Exception e) when (e is IOException or Win32Exception) {
                Log($"淡入淡出参数恢复失败，将在后续操作或退出时重试：{e.Message}", "WARN");
            }
        }
    }
    private void EnsureReady()
    {
        if (!M.IsAlive) throw new IOException("游戏进程已退出。 / Game has exited.");
        ReadState(); if (!Snapshot.CanAct) throw new InvalidOperationException(Snapshot.Detail.Length > 0 ? Snapshot.Detail : "游戏当前无法操作，请等待载入完成。 / Game is not ready for this action.");
        if (operationSaveSlot < 0) operationSaveSlot = Snapshot.SaveSlot;
    }
    public void ActivateGame()
    {
        try {
            if (memory?.IsAlive != true) return;
            nint window = memory.Process.MainWindowHandle;
            if (window != 0) { Native.ShowWindow(window, 9); Native.SetForegroundWindow(window); }
        } catch (InvalidOperationException) { }
    }
    private async Task ExecuteAsync(string title, Func<CancellationToken, Task> action)
    {
        if (!await operation.WaitAsync(0)) throw new InvalidOperationException("已有操作正在进行，请等待完成。 / An operation is already running.");
        Busy = true; BusyChanged?.Invoke(true);
        operationSaveSlot = -1;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try { Log($"开始：{title}。"); await action(timeout.Token); Log($"完成：{title}。"); }
        catch (OperationCanceledException) { Log($"{title} 已取消或超时。", "WARN"); throw; }
        catch (Exception e) { Log($"{title}失败：{e.Message}", "ERROR"); throw; }
        finally { pendingFood = null; operationSaveSlot = -1; Busy = false; BusyChanged?.Invoke(false); operation.Release(); }
    }
    private async Task WaitAsync(Func<bool> condition, CancellationToken token, int seconds, string error)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed.TotalSeconds < seconds) {
            token.ThrowIfCancellationRequested();
            if (!M.IsAlive) throw new IOException("游戏进程已退出。 / Game has exited.");
            try {
                if (operationSaveSlot >= 0 && M.Int32(M.Pointer(G(SaveRoot)) + 0xA0) != operationSaveSlot)
                    throw new InvalidOperationException("猎人存档已切换，操作已停止。 / Hunter save changed; operation stopped.");
                if (condition()) return;
            } catch (IOException) { }
            ApplyPendingFoodWhileLoading(); await Task.Delay(100, token);
        }
        throw new TimeoutException(error);
    }
    public async Task StopAsync()
    {
        lifetime.Cancel(); await operation.WaitAsync();
        try {
            RestoreFadeChanges(temporaryChanges.ToArray());
            temporaryChanges.Clear(); memory?.Dispose(); memory = null;
        } finally { operation.Release(); }
    }
    public void Dispose() { memory?.Dispose(); lifetime.Dispose(); operation.Dispose(); }
    private sealed class RestoreScope(Action restore) : IDisposable { public void Dispose() => restore(); }
}

internal sealed class MemoryBatch(ProcessMemory memory)
{
    private readonly List<(long Address, byte[] Value, byte[] Original)> writes = [];
    public void Add(long address, byte[] value) => writes.Add((address, value, memory.Read(address, value.Length)));
    public void Int32(long address, int value) => Add(address, BitConverter.GetBytes(value));
    public void Float(long address, float value) => Add(address, BitConverter.GetBytes(value));
    public void Pointer(long address, long value) => Add(address, BitConverter.GetBytes(value));
    public void Byte(long address, byte value) => Add(address, [value]);
    public void Commit()
    {
        int applied = 0;
        try { foreach (var write in writes) { memory.Write(write.Address, write.Value); applied++; } }
        catch (Exception failure) {
            Exception? rollbackFailure = null;
            for (int i = applied - 1; i >= 0; i--) {
                try { if (memory.Read(writes[i].Address, writes[i].Value.Length).SequenceEqual(writes[i].Value)) memory.Write(writes[i].Address, writes[i].Original); }
                catch (IOException e) { rollbackFailure = e; }
            }
            if (rollbackFailure != null) throw new AggregateException("写入失败且部分回退失败。 / Write and rollback failed.", failure, rollbackFailure);
            throw;
        }
    }
}
