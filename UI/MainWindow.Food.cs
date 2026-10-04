using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Frostbound.Core;
using Frostbound.UI;
using static Frostbound.UI.Elements;

namespace Frostbound;

public partial class MainWindow
{
    internal Loadout? SelectedFoodLoadout() => Runtime.Settings.Loadouts.FirstOrDefault(p => p.Id == Runtime.Settings.SelectedLoadoutId);

    internal void SaveFood(FoodPreset food, Guid? loadoutId)
    {
        food.Validate();
        if (loadoutId is Guid id) {
            var loadout = Runtime.Settings.Loadouts.FirstOrDefault(p => p.Id == id) ?? throw new InvalidOperationException(T("所选套装已不存在。", "This loadout no longer exists.", "選択したセットがありません。"));
            loadout.Food = food;
        } else Runtime.Settings.Food = food;
        Runtime.Save();
        ShowToast(T("猫饭配置已保存。", "Food configuration saved.", "食事設定を保存しました。"));
    }

    private void OpenFoodEditor()
    {
        var dialog = new FoodEditorDialog(this);
        Runtime.Hotkeys.Suspended = true;
        try { dialog.ShowDialog(); } finally { Runtime.Hotkeys.Suspended = false; }
    }

    private FrameworkElement FoodEditorPage()
    {
        var page = Page("PREPARE BEFORE YOU DEPART", T("猫饭编辑", "Food editor", "食事エディター"),
            T("调整猫饭能力与技能，独立使用或保存到综合套装。", "Edit food bonuses and skills, alone or as part of a loadout.", "能力とスキルを編集。独立設定・総合セットの両方に対応。"));
        var targets = new List<Choice<Guid?>> { new(null, T("独立猫饭配置", "Standalone food configuration", "独立した食事設定")) };
        targets.AddRange(Runtime.Settings.Loadouts.Select(p => new Choice<Guid?>(p.Id, p.Name)));
        var target = new SearchChoicePicker<Guid?>(this, T("搜索猫饭配置", "Search food configurations", "食事設定を検索"), targets.ToArray(),
            targets.Any(p => p.Value == Runtime.Settings.SelectedLoadoutId) ? Runtime.Settings.SelectedLoadoutId : null);
        var editorHost = new ContentControl();
        void RefreshFood() {
            var selected = target.SelectedValue is Guid id ? Runtime.Settings.Loadouts.FirstOrDefault(p => p.Id == id)?.Food : null;
            editorHost.Content = new FoodEditorControl(this, selected ?? Runtime.Settings.Food);
        }
        target.SelectionChanged += (_, _) => RefreshFood(); RefreshFood();
        var panel = new StackPanel(); panel.Children.Add(Field(T("选择要编辑的配置", "Configuration to edit", "編集する設定"), target)); panel.Children.Add(editorHost);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 21, 0, 0) };
        var save = Button(T("保存猫饭配置", "Save food", "食事設定を保存"), () => {
            try { SaveFood(((FoodEditorControl)editorHost.Content).Read(), target.SelectedValue); }
            catch (Exception e) { ShowToast(e.Message); }
        }); save.Margin = new Thickness(0, 0, 12, 0); actions.Children.Add(save);
        var apply = Button(T("应用到游戏", "Apply to game", "ゲームに適用"), async () => {
            try { var food = ((FoodEditorControl)editorHost.Content).Read(); SaveFood(food, target.SelectedValue); await Runtime.ApplyFood(food); }
            catch (Exception e) { ShowToast(e.Message); }
        }, primary: true); gameButtons.Add(apply); actions.Children.Add(apply); panel.Children.Add(actions);
        var note = Text(T("应用会写入游戏猫饭配置；生命与耐力上限随下一次场景载入刷新。", "Applies the food configuration; health and stamina caps refresh with the next scene load.", "食事設定を適用します。体力・スタミナ上限は次のシーン読み込みで反映。"), 10, "#6D8CA2"); note.Margin = new Thickness(0, 17, 0, 0); panel.Children.Add(note);
        page.Children.Add(Card(panel, 22)); return page;
    }
}
