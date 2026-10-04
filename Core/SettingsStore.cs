using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Frostbound.Core;

public sealed class SettingsStore
{
    public static readonly JsonSerializerOptions Json = new() {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };
    public string DirectoryPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Frostbound");
    public string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public string? RecoveryMessage { get; private set; }
    public Settings Load()
    {
        Directory.CreateDirectory(DirectoryPath);
        if (!File.Exists(FilePath)) return new();
        try { return Normalize(JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new()); }
        catch (Exception e) when (e is JsonException or IOException or InvalidOperationException) {
            var corrupt = Path.Combine(DirectoryPath, $"settings.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(FilePath, corrupt, true);
            RecoveryMessage = $"配置损坏，已保留原文件：{corrupt}";
            if (File.Exists(FilePath + ".bak")) {
                try { return Normalize(JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath + ".bak"), Json) ?? new()); }
                catch (JsonException) { }
            }
            return new();
        }
    }
    private static Settings Normalize(Settings value)
    {
        if (value.SchemaVersion > 1) throw new InvalidOperationException("配置来自较新版本，请先更新软件。 / This settings format needs a newer app.");
        value.Hotkeys ??= new(); value.Loadouts ??= []; value.CustomQuests ??= []; value.Favorites ??= [];
        if (value.Language is not ("zh" or "en" or "ja")) value.Language = "zh";
        if (!Enum.IsDefined(value.Mode)) value.Mode = RestartMode.Stable;
        try {
            HotkeyBinding.Parse(value.Hotkeys.RestartKeyboard, false);
            HotkeyBinding.Parse(value.Hotkeys.RestartController, true);
            HotkeyBinding.Parse(value.Hotkeys.ResetKeyboard, false);
            HotkeyBinding.Parse(value.Hotkeys.ResetController, true);
        } catch (FormatException e) { throw new InvalidOperationException("配置中的快捷键无效。 / Invalid shortcut settings.", e); }
        foreach (var loadout in value.Loadouts) {
            loadout.Food ??= new(); loadout.EquipmentName ??= ""; loadout.ItemName ??= ""; loadout.Validate();
        }
        foreach (var quest in value.CustomQuests) {
            quest.Chinese ??= ""; quest.English ??= ""; quest.Japanese ??= ""; quest.Category ??= "自定义";
            if (quest.Id is < 101 or > 67809) throw new InvalidOperationException("配置中的任务 ID 无效。");
        }
        return value;
    }
    public void Save(Settings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json), new UTF8Encoding(false));
        if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak", true);
        else File.Move(temporary, FilePath);
    }
    public void Export(Settings settings, string path) => File.WriteAllText(path, JsonSerializer.Serialize(settings, Json), new UTF8Encoding(false));
    public Settings Import(string path) => Normalize(JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json) ?? throw new JsonException("配置为空。"));

    public static string ReadLegacyText(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { return Encoding.GetEncoding(936).GetString(bytes); }
    }

    public int ImportLegacy(string folder, Settings settings)
    {
        var config = Path.Combine(folder, "config.txt");
        var keymap = Path.Combine(folder, "keymap.txt");
        var allsets = Path.Combine(folder, "allmyset.txt");
        var names = Path.Combine(folder, "Q_name.txt");
        if (!new[] { config, keymap, allsets, names }.Any(File.Exists)) throw new IOException("所选目录没有可迁移的旧版配置文件。");
        if (File.Exists(config)) {
            var text = ReadLegacyText(config);
            var lang = Regex.Match(text, @"(?:Language|Languege)\s*:\s*(\w+)", RegexOptions.IgnoreCase).Groups[1].Value;
            settings.Language = lang.ToLowerInvariant() switch { "english" => "en", "japanese" => "ja", _ => "zh" };
            var reset = Regex.IsMatch(text, @"Reset\s*:\s*1", RegexOptions.IgnoreCase);
            var accept = Regex.IsMatch(text, @"Accept only\s*:\s*1", RegexOptions.IgnoreCase);
            settings.Mode = accept ? RestartMode.AcceptOnly : reset ? RestartMode.Stable : RestartMode.Quick;
            var shortcut = text.IndexOf("Shortcuts:", StringComparison.OrdinalIgnoreCase);
            if (shortcut >= 0) ReadKeys(text[(shortcut + 10)..].Split('{')[0], settings);
        }
        if (File.Exists(keymap)) ReadKeys(ReadLegacyText(keymap), settings);
        if (File.Exists(names)) {
            foreach (var quest in ParseQuestNames(ReadLegacyText(names))) {
                settings.CustomQuests.RemoveAll(x => x.Id == quest.Id);
                settings.CustomQuests.Add(quest);
            }
        }
        int count = 0;
        foreach (var path in new[] { config, allsets }.Where(File.Exists)) {
            var text = ReadLegacyText(path);
            var start = text.IndexOf('{');
            if (start < 0) continue;
            using var document = JsonDocument.Parse(text[start..]);
            if (!document.RootElement.TryGetProperty("allsets", out var array)) continue;
            foreach (var element in array.EnumerateArray()) {
                string S(string key) => element.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";
                int I(string key, int fallback = 0) => element.TryGetProperty(key, out var v) && v.TryGetInt32(out var n) ? n : fallback;
                uint U(string key) => element.TryGetProperty(key, out var v) && v.TryGetUInt32(out var n) ? n : 0;
                var boost = I("能力提升", 4);
                var set = new Loadout {
                    Name = S("综合套装名称"), EquipmentName = S("装备预设套装"), ItemName = S("道具预设套装"),
                    QuestId = I("任务ID"), SaveSlot = I("数据编号", -1), UserId = U("用户ID"),
                    Food = new() { Enabled = I("猫饭无效") == 0, Health = boost == 0 ? 0 : 5, Stamina = boost == 0 ? 0 : 2,
                        Attack = boost == 4 ? 3 : 0, Defense = boost == 8 ? 3 : 0, Resistance = boost == 12 ? 3 : 0,
                        Skill1 = I("技能1", -1), Skill2 = I("技能2", -1), Skill3 = I("技能3", -1) }
                };
                if (set.QuestId >= 100000) { set.QuestId -= 100000; set.Wingdrake = true; }
                if (string.IsNullOrWhiteSpace(set.Name)) continue;
                set.Validate();
                var old = settings.Loadouts.FindIndex(x => x.Name == set.Name && x.SaveSlot == set.SaveSlot && x.UserId == set.UserId);
                if (old >= 0) { set.Id = settings.Loadouts[old].Id; settings.Loadouts[old] = set; }
                else { settings.Loadouts.Add(set); count++; }
            }
        }
        return count;
    }
    private static void ReadKeys(string text, Settings settings)
    {
        var lines = text.Replace("\r", "").Trim('\n').Split('\n');
        if (lines.Length > 0) settings.Hotkeys.RestartKeyboard = lines[0].Trim();
        if (lines.Length > 1) settings.Hotkeys.RestartController = lines[1].Trim();
        if (lines.Length > 2) settings.Hotkeys.ResetKeyboard = lines[2].Trim();
        if (lines.Length > 3) settings.Hotkeys.ResetController = lines[3].Trim();
    }
    public static List<Quest> ParseQuestNames(string text)
    {
        string category = "自定义";
        var quests = new List<Quest>();
        foreach (var raw in text.Split('\n')) {
            string line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { category = line[1..^1]; continue; }
            var split = line.IndexOf(':');
            if (split < 0 || !int.TryParse(line[..split], out var id)) continue;
            bool wingdrake = id >= 100000; if (wingdrake) id -= 100000;
            if (id is < 101 or > 67809 || line[(split + 1)..].Trim().Length == 0) continue;
            quests.Add(new() { Id = id, Category = category, Chinese = line[(split + 1)..].Trim(), Wingdrake = wingdrake });
        }
        return quests;
    }
    public static List<T> ReadResource<T>(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Frostbound.Assets.{name}") ?? throw new IOException($"缺少资源：{name}");
        return JsonSerializer.Deserialize<List<T>>(stream, Json) ?? [];
    }
}
