using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Frostbound.Core;

public sealed class AppRuntime : IDisposable
{
    public SettingsStore Store { get; } = new();
    public Settings Settings { get; set; }
    public GameEngine Engine { get; } = new();
    public ObservableCollection<LogEntry> Logs { get; } = [];
    public List<Quest> Quests { get; private set; } = [];
    public List<FoodSkill> FoodSkills { get; }
    public HotkeyService Hotkeys { get; }
    public event Action<GameSnapshot>? StateChanged;
    public event Action<string>? Toast;
    private readonly Dispatcher dispatcher;
    private readonly CancellationTokenSource stop = new();
    private Task? polling;
    public AppRuntime(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        Settings = Store.Load(); FoodSkills = SettingsStore.ReadResource<FoodSkill>("food-skills.json"); ReloadQuests();
        Engine.Logged += (level, message) => dispatcher.BeginInvoke(() => AddLog(level, message));
        Engine.SnapshotChanged += state => dispatcher.BeginInvoke(() => StateChanged?.Invoke(state));
        Hotkeys = new(Engine, () => Settings.Hotkeys);
        Hotkeys.Triggered += async restart => { if (restart) await Restart(); else await Reset(); };
        if (Store.RecoveryMessage != null) AddLog("WARN", Store.RecoveryMessage);
        AddLog("INFO", "霜序已启动，等待游戏进程。 / Frostbound is ready.");
    }
    public void Start() => polling = Task.Run(async () => {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try {
            do {
                try {
                    await Engine.PollAsync();
                    if (Settings.ChatCommands) {
                        var command = Engine.ReadChatCommand();
                        if (command != null) _ = dispatcher.BeginInvoke(async () => await HandleChat(command));
                    }
                } catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                catch (Exception e) { _ = dispatcher.BeginInvoke(() => AddLog("ERROR", e.Message)); }
            } while (await timer.WaitForNextTickAsync(stop.Token));
        } catch (OperationCanceledException) { }
    });
    public void ReloadQuests()
    {
        var builtIn = SettingsStore.ReadResource<Quest>("quests.json");
        foreach (var custom in Settings.CustomQuests) {
            var old = builtIn.FirstOrDefault(q => q.Id == custom.Id);
            if (old == null) builtIn.Add(custom);
            else {
                if (custom.Chinese.Length > 0) old.Chinese = custom.Chinese;
                if (custom.English.Length > 0) old.English = custom.English;
                if (custom.Japanese.Length > 0) old.Japanese = custom.Japanese;
                old.Wingdrake = custom.Wingdrake; old.Category = custom.Category;
            }
        }
        Quests = builtIn;
    }
    public void Save()
    {
        try { Store.Save(Settings); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { AddLog("ERROR", e.Message); Toast?.Invoke(e.Message); }
    }
    public async Task Retry() { try { await Task.Run(() => Engine.PollAsync(true)); } catch (Exception e) { Toast?.Invoke(e.Message); } }
    public async Task Restart()
    {
        var s = Settings; int id = s.SelectedQuestId;
        var quest = Quests.FirstOrDefault(q => q.Id == id);
        await Run(() => Engine.RestartAsync(id, s.Mode, s.Wingdrake || quest?.Wingdrake == true, s.FastFade));
    }
    public Task Reset() => Run(() => Engine.ResetAsync(Settings.FastFade));
    public Task Apply(Loadout loadout, bool restart) => Run(() => Engine.ApplyLoadoutAsync(loadout, restart, Settings.Mode, Settings.Wingdrake, Settings.FastFade));
    private async Task Run(Func<Task> action)
    {
        try {
            if (Settings.FocusGameOnAction && Engine.Snapshot.CanAct) Engine.ActivateGame();
            await Task.Run(action);
        }
        catch (Exception e) { Toast?.Invoke(e.Message); }
    }
    private async Task HandleChat(string text)
    {
        char[] separators = ['，', ',', '、']; int separator = text.IndexOfAny(separators);
        string questText = (separator >= 0 ? text[..separator] : text).Trim();
        string setText = separator >= 0 ? text[(separator + 1)..].Trim() : "";
        var quest = Quests.FirstOrDefault(q => new[] { q.Chinese, q.English, q.Japanese, q.Id.ToString() }.Contains(questText, StringComparer.OrdinalIgnoreCase));
        Loadout? loadout = null;
        if (setText.Length > 0) loadout = Settings.Loadouts.FirstOrDefault(p => p.Name == setText &&
            (p.SaveSlot < 0 || p.SaveSlot == Engine.Snapshot.SaveSlot) && (p.UserId == 0 || p.UserId == Engine.Snapshot.UserId));
        if (quest == null && loadout == null) return;
        if (setText.Length > 0 && loadout == null) { AddLog("WARN", $"聊天指令中的套装未找到：{setText}"); return; }
        AddLog("INFO", "收到匹配的聊天快捷指令。");
        if (quest != null) Settings.SelectedQuestId = quest.Id;
        if (loadout != null) {
            var copy = System.Text.Json.JsonSerializer.Deserialize<Loadout>(System.Text.Json.JsonSerializer.Serialize(loadout, SettingsStore.Json), SettingsStore.Json)!;
            if (quest != null) { copy.QuestId = quest.Id; copy.Wingdrake = quest.Wingdrake; }
            await Apply(copy, quest != null);
        } else await Restart();
    }
    public void AddLog(string level, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, message); Logs.Insert(0, entry);
        while (Logs.Count > 300) Logs.RemoveAt(Logs.Count - 1);
        try {
            var directory = Path.Combine(Store.DirectoryPath, "logs"); Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, $"{DateTime.Now:yyyy-MM-dd}.log"), entry + Environment.NewLine);
        } catch (IOException) { }
    }
    public async Task Stop()
    {
        stop.Cancel(); Hotkeys.Dispose(); await Engine.StopAsync();
        if (polling != null) await polling; Save();
    }
    public void Dispose() { stop.Dispose(); Engine.Dispose(); }
}
