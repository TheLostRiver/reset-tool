using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Frostbound.Core;
using static Frostbound.UI.Elements;

namespace Frostbound.UI;

internal sealed class FoodEditorControl : StackPanel
{
    private readonly CheckBox enabled;
    private readonly ComboBox health, stamina, attack, defense, resistance;
    private readonly SearchChoicePicker<int> skill1, skill2, skill3;

    public FoodEditorControl(MainWindow owner, FoodPreset food)
    {
        string T(string zh, string en, string ja) => owner.T(zh, en, ja);
        enabled = new CheckBox { Content = T("启用猫饭效果", "Enable food effects", "食事効果を有効化"), IsChecked = food.Enabled,
            FontSize = 13, Margin = new Thickness(0, 0, 0, 20) };
        Children.Add(enabled);
        health = Choices(Enumerable.Range(0, 6).Select(n => new Choice<int>(n, $"+{n * 10} HP")).ToArray(), food.Health);
        stamina = Choices(Enumerable.Range(0, 3).Select(n => new Choice<int>(n, $"+{n * 25} " + T("耐力", "Stamina", "スタミナ"))).ToArray(), food.Stamina);
        Choice<int>[] Buffs() => [new(0, T("无", "None", "なし")), new(1, T("小", "Small", "小")), new(2, T("中", "Medium", "中")), new(3, T("大", "Large", "大"))];
        attack = Choices(Buffs(), food.Attack); defense = Choices(Buffs(), food.Defense); resistance = Choices(Buffs(), food.Resistance);
        Choice<int>[] Skills() => owner.Runtime.FoodSkills.Select(s => new Choice<int>(s.Id, $"{s.Name(owner.Runtime.Settings.Language)}  ·  {s.Id}")).ToArray();
        string SearchSkill(Choice<int> choice) {
            var skill = owner.Runtime.FoodSkills.First(s => s.Id == choice.Value);
            return $"{skill.Id} {skill.Chinese} {skill.English} {skill.Japanese}";
        }
        skill1 = new(owner, T("选择猫饭技能 1", "Choose food skill 1", "食事スキル 1 を選択"), Skills(), food.Skill1, SearchSkill);
        skill2 = new(owner, T("选择猫饭技能 2", "Choose food skill 2", "食事スキル 2 を選択"), Skills(), food.Skill2, SearchSkill);
        skill3 = new(owner, T("选择猫饭技能 3", "Choose food skill 3", "食事スキル 3 を選択"), Skills(), food.Skill3, SearchSkill);
        var fields = new StackPanel();
        fields.Children.Add(Columns(Field(T("生命值提升", "Health bonus", "体力増加"), health), Field(T("耐力提升", "Stamina bonus", "スタミナ増加"), stamina)));
        fields.Children.Add(Three(Field(T("攻击力", "Attack", "攻撃力"), attack), Field(T("防御力", "Defense", "防御力"), defense), Field(T("属性耐性", "Element resistance", "属性耐性"), resistance)));
        fields.Children.Add(Field(T("猫饭技能 1", "Food skill 1", "食事スキル 1"), skill1));
        fields.Children.Add(Field(T("猫饭技能 2", "Food skill 2", "食事スキル 2"), skill2));
        fields.Children.Add(Field(T("猫饭技能 3", "Food skill 3", "食事スキル 3"), skill3));
        fields.IsEnabled = food.Enabled;
        enabled.Click += (_, _) => fields.IsEnabled = enabled.IsChecked == true;
        Children.Add(fields);
        Children.Add(Text(T("关闭猫饭效果后，应用时会清除猫饭能力与技能。", "Applying disabled food clears its bonuses and skills.", "無効にして適用すると、食事の能力とスキルを解除します。"), 10, "#7894A8"));
    }

    public FoodPreset Read()
    {
        var result = new FoodPreset {
            Enabled = enabled.IsChecked == true,
            Health = (int)health.SelectedValue, Stamina = (int)stamina.SelectedValue,
            Attack = (int)attack.SelectedValue, Defense = (int)defense.SelectedValue, Resistance = (int)resistance.SelectedValue,
            Skill1 = skill1.SelectedValue, Skill2 = skill2.SelectedValue, Skill3 = skill3.SelectedValue
        };
        result.Validate(); return result;
    }
}

internal sealed class FoodEditorDialog : StudioDialog
{
    public FoodEditorDialog(MainWindow owner) : base(owner, owner.T("猫饭编辑", "Food editor", "食事エディター"), 650, 700)
    {
        var source = owner.SelectedFoodLoadout();
        var form = new FoodEditorControl(owner, source?.Food ?? owner.Runtime.Settings.Food);
        var body = new StackPanel();
        var context = Text(source == null ? T("编辑独立猫饭配置", "Editing your standalone food configuration", "独立した食事設定を編集") :
            T($"编辑综合套装：{source.Name}", $"Editing loadout: {source.Name}", $"編集中のセット：{source.Name}"), 11, "#8FADBF");
        context.Margin = new Thickness(0, 0, 0, 20); body.Children.Add(context); body.Children.Add(form); Body(body, true);
        Cancel();
        var save = Button(T("保存", "Save", "保存"), () => {
            try { owner.SaveFood(form.Read(), source?.Id); DialogResult = true; }
            catch (Exception e) { Error.Text = e.Message; }
        }); save.Margin = new Thickness(0, 0, 10, 0); Actions.Children.Add(save);
        var apply = Button(T("应用到游戏", "Apply to game", "ゲームに適用"), async () => {
            try { var value = form.Read(); owner.SaveFood(value, source?.Id); await owner.Runtime.ApplyFood(value); }
            catch (Exception e) { Error.Text = e.Message; }
        }, primary: true);
        void StateChanged(GameSnapshot state) => apply.IsEnabled = state.CanAct && !owner.Runtime.Engine.Busy;
        owner.Runtime.StateChanged += StateChanged; StateChanged(owner.Runtime.Engine.Snapshot);
        Closed += (_, _) => owner.Runtime.StateChanged -= StateChanged; Actions.Children.Add(apply);
    }
}
