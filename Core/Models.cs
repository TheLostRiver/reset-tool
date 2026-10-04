using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Frostbound.Core;

public enum RestartMode { Stable, Quick, AcceptOnly }
public enum InterfaceStyle { Full, Compact }
public enum ConnectionState { Waiting, Reading, Ready, Unsupported, AccessDenied, Faulted }

public sealed class Quest
{
    public int Id { get; set; }
    public string Category { get; set; } = "其他";
    public string Chinese { get; set; } = "";
    public string English { get; set; } = "";
    public string Japanese { get; set; } = "";
    public bool Wingdrake { get; set; }
    public string Name(string language) => language switch {
        "en" when English.Length > 0 => English,
        "ja" when Japanese.Length > 0 => Japanese,
        _ => Chinese
    };
    public override string ToString() => $"{Id:D5}  ·  {Chinese}";
}

public sealed class FoodSkill
{
    public int Id { get; set; }
    public string Chinese { get; set; } = "";
    public string English { get; set; } = "";
    public string Japanese { get; set; } = "";
    public string Name(string language) => language switch { "en" => English, "ja" => Japanese, _ => Chinese };
}

public sealed class FoodPreset
{
    public bool Enabled { get; set; } = true;
    public int Health { get; set; } = 5;
    public int Stamina { get; set; } = 2;
    public int Attack { get; set; } = 3;
    public int Defense { get; set; }
    public int Resistance { get; set; }
    public int Skill1 { get; set; } = 39;
    public int Skill2 { get; set; } = 32;
    public int Skill3 { get; set; } = 34;
    public int[] ToArray() => Enabled ? [Health, Stamina, Attack, Defense, Resistance, Skill1, Skill2, Skill3] : [0, 0, 0, 0, 0, -1, -1, -1];
    public void Validate()
    {
        if (Health is < 0 or > 5 || Stamina is < 0 or > 2 || Attack is < 0 or > 3 || Defense is < 0 or > 3 || Resistance is < 0 or > 3)
            throw new InvalidOperationException("猫饭能力值超出游戏支持范围。 / Invalid food stats.");
        if (new[] { Skill1, Skill2, Skill3 }.Any(x => x is < -1 or > 56 || x == 37))
            throw new InvalidOperationException("猫饭技能编号无效。 / Invalid food skill ID.");
    }
}

public sealed class Loadout
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string EquipmentName { get; set; } = "";
    public string ItemName { get; set; } = "";
    public int EquipmentSlot { get; set; }
    public int ItemSlot { get; set; }
    public int QuestId { get; set; }
    public bool Wingdrake { get; set; }
    public bool? LinkRadialMenu { get; set; }
    public FoodPreset Food { get; set; } = new();
    public int SaveSlot { get; set; } = -1;
    public uint UserId { get; set; }
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidOperationException("请填写套装名称。 / A name is required.");
        if (Name.Length > 64) throw new InvalidOperationException("套装名称最多 64 个字符。 / Name is too long.");
        if (EquipmentSlot is < 0 or > 224 || ItemSlot is < 0 or > 80)
            throw new InvalidOperationException("装备编号应为 0–224，道具编号应为 0–80。 / Invalid loadout slot.");
        if (QuestId != 0 && QuestId is < 101 or > 67809)
            throw new InvalidOperationException("任务 ID 超出支持范围。 / Invalid quest ID.");
        if (SaveSlot is < -1 or > 2) throw new InvalidOperationException("存档编号应为 0、1、2，或不绑定。 / Invalid save slot.");
        Food.Validate();
    }
    public override string ToString() => Name;
}

public sealed class HotkeySettings
{
    public string RestartKeyboard { get; set; } = "Ctrl+R";
    public string RestartController { get; set; } = "LS+RS";
    public string ResetKeyboard { get; set; } = "Num+";
    public string ResetController { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool ForegroundOnly { get; set; } = true;
}

public sealed class Settings
{
    public int SchemaVersion { get; set; } = 1;
    public string Language { get; set; } = "zh";
    public InterfaceStyle InterfaceStyle { get; set; } = InterfaceStyle.Full;
    public FoodPreset Food { get; set; } = new();
    public string GamePath { get; set; } = @"D:\Software\Steam\steamapps\common\Monster Hunter World\MonsterHunterWorld.exe";
    public RestartMode Mode { get; set; } = RestartMode.Stable;
    public bool Wingdrake { get; set; }
    public bool FastFade { get; set; } = true;
    public bool ChatCommands { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool FocusGameOnAction { get; set; } = true;
    public int SelectedQuestId { get; set; }
    public Guid? SelectedLoadoutId { get; set; }
    public HotkeySettings Hotkeys { get; set; } = new();
    public List<int> Favorites { get; set; } = [66845, 66861];
    public List<Loadout> Loadouts { get; set; } = [];
    public List<Quest> CustomQuests { get; set; } = [];
}

public sealed record GameSnapshot(ConnectionState State, int ProcessId = 0, long ModuleBase = 0,
    string Detail = "", int QuestState = 0, bool Loading = false, int QuestId = 0,
    int SaveSlot = -1, uint UserId = 0, string HunterName = "", string Build = "", bool IsActionable = true)
{
    public bool CanAct => State == ConnectionState.Ready && !Loading && SaveSlot >= 0 && IsActionable && QuestState is 1 or 2 or 4 or 5 or 13;
}
public sealed record GameLoadoutSlot(int Number, string Name)
{
    public override string ToString() => $"{Number:D3}  ·  {Name}";
}
public sealed record LogEntry(DateTime Time, string Level, string Message)
{
    public override string ToString() => $"{Time:HH:mm:ss}  [{Level}]  {Message}";
}
