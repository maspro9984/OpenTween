// OpenTween - Client of Twitter
// All rights reserved.
//
// This file is part of OpenTween.
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation; either version 3 of the License, or (at your option)
// any later version.
//
// This program is distributed in the hope that it will be useful, but
// WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
// or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License
// for more details.
//
// You should have received a copy of the GNU General Public License along
// with this program. If not, see <http://www.gnu.org/licenses/>, or write to
// the Free Software Foundation, Inc., 51 Franklin Street - Fifth Floor,
// Boston, MA 02110-1301, USA.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using OpenTween.Models;

namespace OpenTween
{
    /// <summary>
    /// タブ毎の表示フィルタ (RT・広告・フォロー外・非表示カテゴリの発言を一覧に表示しない機能)
    /// </summary>
    public partial class TweenMain
    {
        private const Keys ToggleHideFilterShortcutKeys = Keys.Control | Keys.Alt | Keys.F;

        private ToolStripMenuItem? hideFilterMenuItem;
        private ToolStripMenuItem? addToHideCategoryMenuItem;

        private void InitializeHideFilter()
        {
            this.hideFilterMenuItem = new ToolStripMenuItem("表示フィルタ");

            // サブメニューの矢印を表示させるため、ダミーの項目を入れておく (DropDownOpening で作り直す)
            this.hideFilterMenuItem.DropDownItems.Add(new ToolStripMenuItem("-"));
            this.hideFilterMenuItem.DropDownOpening += (s, e) => this.BuildHideFilterMenu();

            var index = this.ContextMenuTabProperty.Items.IndexOf(this.FilterEditMenuItem);
            this.ContextMenuTabProperty.Items.Insert(index + 1, this.hideFilterMenuItem);

            // 発言の右クリックメニュー: 発言者を非表示カテゴリに追加する
            this.addToHideCategoryMenuItem = new ToolStripMenuItem("非表示カテゴリに追加");
            this.addToHideCategoryMenuItem.DropDownItems.Add(new ToolStripMenuItem("-"));
            this.addToHideCategoryMenuItem.DropDownOpening += (s, e) => this.BuildAddToHideCategoryMenu();

            var operateIndex = this.ContextMenuOperate.Items.IndexOf(this.ToolStripMenuItem7);
            this.ContextMenuOperate.Items.Insert(operateIndex + 1, this.addToHideCategoryMenuItem);
            this.ContextMenuOperate.Opening += (s, e) =>
                this.addToHideCategoryMenuItem.Enabled = this.GetHideCategoryTargetScreenNames().Length > 0;

            this.ApplyHideFilterAllTabs();
        }

        /// <summary>選択中の発言の発言者 (リツイートの場合は元の発言者) のスクリーンネーム</summary>
        private string[] GetHideCategoryTargetScreenNames()
            => this.CurrentTab.SelectedPosts
                .Select(x => x.ScreenName)
                .Where(x => !MyCommon.IsNullOrEmpty(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        private void BuildAddToHideCategoryMenu()
        {
            var items = this.addToHideCategoryMenuItem!.DropDownItems;
            items.Clear();

            var screenNames = this.GetHideCategoryTargetScreenNames();
            if (screenNames.Length == 0)
                return;

            var targetText = screenNames.Length == 1 ? $"@{screenNames[0]}" : $"{screenNames.Length}人";
            items.Add(new ToolStripMenuItem($"対象: {targetText}") { Enabled = false });
            items.Add(new ToolStripSeparator());

            foreach (var category in this.settings.Common.HideCategories)
            {
                var categoryName = category.Name;
                var registered = new HashSet<string>(category.ScreenNames, StringComparer.OrdinalIgnoreCase);
                var allRegistered = screenNames.All(x => registered.Contains(x));

                // 全員が登録済みのカテゴリはチェックを付け、クリックで登録を解除する
                var item = new ToolStripMenuItem(categoryName) { Checked = allRegistered };
                item.Click += (s, e) =>
                {
                    if (allRegistered)
                        this.RemoveFromHideCategory(categoryName, screenNames);
                    else
                        this.AddToHideCategory(categoryName, screenNames);
                };
                items.Add(item);
            }

            if (this.settings.Common.HideCategories.Count > 0)
                items.Add(new ToolStripSeparator());

            var newCategoryItem = new ToolStripMenuItem("新しいカテゴリを作成して追加...");
            newCategoryItem.Click += (s, e) => this.AddToNewHideCategory(screenNames);
            items.Add(newCategoryItem);
        }

        private void AddToNewHideCategory(string[] screenNames)
        {
            if (InputDialog.Show(this, "カテゴリ名を入力してください (例: ニュース)", "非表示カテゴリの作成", out var inputText) != DialogResult.OK)
                return;

            var categoryName = inputText.Trim();
            if (categoryName.Length == 0)
                return;

            // 新しく作成したカテゴリは現在のタブで非表示にする (追加した発言がその場で隠れるようにする)
            var hideSettings = this.CurrentTab.HideSettings;
            if (!hideSettings.HiddenCategories.Contains(categoryName))
                hideSettings.HiddenCategories = hideSettings.HiddenCategories.Append(categoryName).ToArray();
            hideSettings.Enabled = true;

            this.AddToHideCategory(categoryName, screenNames);
            this.SaveConfigsTabs();
        }

        private void AddToHideCategory(string categoryName, string[] screenNames)
        {
            this.UpdateHideCategory(categoryName, current => current.Concat(screenNames));

            var targetText = screenNames.Length == 1 ? $"@{screenNames[0]}" : $"{screenNames.Length}人";
            var hideSettings = this.CurrentTab.HideSettings;
            var message = $"{targetText} を非表示カテゴリ「{categoryName}」に追加しました";

            if (!hideSettings.HiddenCategories.Contains(categoryName))
                message += $" (このタブで隠すには、タブの右クリック → 表示フィルタで「{categoryName}」を選択してください)";
            else if (!hideSettings.Enabled)
                message += " (このタブはフィルター表示が OFF です。Ctrl+Alt+F で ON にできます)";

            this.StatusLabel.Text = message;
        }

        private void RemoveFromHideCategory(string categoryName, string[] screenNames)
        {
            var removing = new HashSet<string>(screenNames, StringComparer.OrdinalIgnoreCase);
            this.UpdateHideCategory(categoryName, current => current.Where(x => !removing.Contains(x)));

            var targetText = screenNames.Length == 1 ? $"@{screenNames[0]}" : $"{screenNames.Length}人";
            this.StatusLabel.Text = $"{targetText} を非表示カテゴリ「{categoryName}」から外しました";
        }

        /// <summary>非表示カテゴリのユーザーを変更し (カテゴリが無ければ作成し)、全てのタブに反映する</summary>
        private void UpdateHideCategory(string categoryName, Func<IEnumerable<string>, IEnumerable<string>> updateScreenNames)
        {
            var tabs = this.statuses.Tabs.ToArray();

            this.ChangeHideFilter(tabs, () =>
            {
                var categories = this.settings.Common.HideCategories;
                var index = categories.FindIndex(x => x.Name == categoryName);
                var current = index != -1 ? categories[index].ScreenNames : Array.Empty<string>();

                var updated = new HideCategory
                {
                    Name = categoryName,
                    ScreenNames = updateScreenNames(current)
                        .Select(PostHideFilter.NormalizeScreenName)
                        .Where(x => x.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                };

                if (index != -1)
                    categories[index] = updated;
                else
                    categories.Add(updated);

                this.ApplyHideFilterAllTabs();
            });

            this.MarkSettingCommonModified();
        }

        private void BuildHideFilterMenu()
        {
            var items = this.hideFilterMenuItem!.DropDownItems;
            items.Clear();

            var tabName = MyCommon.IsNullOrEmpty(this.rclickTabName) ? this.CurrentTabName : this.rclickTabName;
            if (!this.statuses.Tabs.TryGetValue(tabName, out var tab))
                return;

            var hideSettings = tab.HideSettings;

            // フィルター表示の on/off (下の各条件・カテゴリをまとめて適用するか)
            var toggleItem = new ToolStripMenuItem("フィルター表示") { Checked = hideSettings.IsActive };
            toggleItem.ShortcutKeyDisplayString = "Ctrl+Alt+F";
            toggleItem.Click += (s, e) => this.ToggleHideFilter(tabName);
            items.Add(toggleItem);

            items.Add(new ToolStripSeparator());
            items.Add(this.CreateHideFilterMenuItem(
                "リツイートを隠す", hideSettings.HideRetweets, tabName, (x, v) => x.HideRetweets = v));
            items.Add(this.CreateHideFilterMenuItem(
                "広告を隠す", hideSettings.HidePromoted, tabName, (x, v) => x.HidePromoted = v));
            items.Add(this.CreateHideFilterMenuItem(
                "フォローしていない人の発言を隠す", hideSettings.HideNonFollowing, tabName, (x, v) => x.HideNonFollowing = v));

            items.Add(new ToolStripSeparator());

            var categories = this.settings.Common.HideCategories;
            if (categories.Count == 0)
            {
                items.Add(new ToolStripMenuItem("(非表示カテゴリなし)") { Enabled = false });
            }
            else
            {
                foreach (var category in categories)
                {
                    var categoryName = category.Name;
                    var isHidden = hideSettings.HiddenCategories.Contains(categoryName);
                    var text = $"「{categoryName}」を隠す ({category.ScreenNames.Length}人)";

                    items.Add(this.CreateHideFilterMenuItem(text, isHidden, tabName, (x, v) =>
                    {
                        x.HiddenCategories = v
                            ? x.HiddenCategories.Append(categoryName).Distinct().ToArray()
                            : x.HiddenCategories.Where(y => y != categoryName).ToArray();
                    }));
                }
            }

            items.Add(new ToolStripSeparator());

            var editItem = new ToolStripMenuItem("非表示カテゴリの編集...");
            editItem.Click += (s, e) => this.EditHideCategories();
            items.Add(editItem);
        }

        private ToolStripMenuItem CreateHideFilterMenuItem(
            string text, bool isChecked, string tabName, Action<TabHideSettings, bool> apply)
        {
            var item = new ToolStripMenuItem(text) { Checked = isChecked };
            item.Click += (s, e) => this.UpdateHideSettings(tabName, x =>
            {
                apply(x, !isChecked);

                // フィルター表示が off の状態で条件を追加した場合は、その条件が効くよう on にする
                if (!isChecked)
                    x.Enabled = true;
            });
            return item;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // タブの見出しなど、ShortcutCommand の KeyDown が届かないコントロールにフォーカスがある場合も
            // 動作させるため、フォーム全体でショートカットキーを処理する
            if (keyData == ToggleHideFilterShortcutKeys)
            {
                this.ToggleHideFilter(this.CurrentTabName);
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>フィルター表示の on/off を切り替える</summary>
        private void ToggleHideFilter(string tabName)
        {
            if (!this.statuses.Tabs.TryGetValue(tabName, out var tab))
                return;

            var enable = !tab.HideSettings.IsActive;
            this.UpdateHideSettings(tabName, x =>
            {
                // 条件が何も設定されていない場合は、フォローしてる人の発言のみ表示 (RT・広告・フォロー外を隠す) を初期値とする
                if (enable && !x.HasAnyCondition)
                    x.FollowingOnly = true;

                x.Enabled = enable;
            });

            this.StatusLabel.Text = enable
                ? $"{tabName}: フィルター表示 ON ({this.FormatHideConditions(tab.HideSettings)})"
                : $"{tabName}: フィルター表示 OFF";
        }

        private string FormatHideConditions(TabHideSettings hideSettings)
        {
            var conditions = new List<string>();
            if (hideSettings.HideRetweets)
                conditions.Add("RT");
            if (hideSettings.HidePromoted)
                conditions.Add("広告");
            if (hideSettings.HideNonFollowing)
                conditions.Add("フォロー外");
            conditions.AddRange(hideSettings.HiddenCategories.Select(x => $"「{x}」"));

            return string.Join("・", conditions) + " を非表示";
        }

        private void UpdateHideSettings(string tabName, Action<TabHideSettings> modify)
        {
            if (!this.statuses.Tabs.TryGetValue(tabName, out var tab))
                return;

            this.ChangeHideFilter(new[] { tab }, () =>
            {
                modify(tab.HideSettings);
                this.ApplyHideFilter(tab);
            });

            this.SaveConfigsTabs();
        }

        /// <summary>
        /// 表示フィルタを変更し、表示する発言が変化したタブの一覧を更新する
        /// </summary>
        /// <remarks>
        /// 一覧の選択状態・スクロール位置はインデックスから発言を特定して保存するため、
        /// フィルタを変更する前に保存しておく必要がある
        /// </remarks>
        private void ChangeHideFilter(IEnumerable<TabModel> tabs, Action changeFilter)
        {
            var currentTabName = this.CurrentTabName;
            this.listViewState.TryGetValue(currentTabName, out var currentState);
            currentState?.Save(this.ListLockMenuItem.Checked);

            changeFilter();

            foreach (var tab in tabs)
            {
                var tabName = tab.TabName;

                if (this.listCaches.TryGetValue(tabName, out var cache))
                {
                    if (tabName == currentTabName && currentState != null)
                    {
                        using (ControlTransaction.Update(this.CurrentListView))
                        {
                            cache.PurgeCache();
                            cache.UpdateListSize();
                            currentState.RestoreSelection();
                        }

                        currentState.RestoreScroll();
                    }
                    else
                    {
                        cache.PurgeCache();
                        cache.UpdateListSize();
                    }
                }

                this.ListTab.SetTabUnreadState(tabName, tab.UnreadCount > 0);
            }

            this.SetMainWindowTitle();
            this.SetStatusLabelUrl();
        }

        private void ApplyHideFilter(TabModel tab)
        {
            var hideFilter = PostHideFilter.Create(tab.HideSettings, this.settings.Common.HideCategories);
            this.statuses.SetHideFilter(tab, hideFilter);
        }

        private void ApplyHideFilterAllTabs()
        {
            foreach (var tab in this.statuses.Tabs)
                this.ApplyHideFilter(tab);
        }

        private void EditHideCategories()
        {
            using var dialog = new HideCategoryDialog(this.settings.Common.HideCategories);
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            var tabs = this.statuses.Tabs.ToArray();

            this.ChangeHideFilter(tabs, () =>
            {
                this.settings.Common.HideCategories = dialog.Categories;

                // 名前が変更・削除されたカテゴリをタブの設定に反映する
                var existingNames = new HashSet<string>(dialog.Categories.Select(x => x.Name));
                foreach (var tab in tabs)
                {
                    tab.HideSettings.HiddenCategories = tab.HideSettings.HiddenCategories
                        .Select(x => dialog.RenamedCategories.TryGetValue(x, out var newName) ? newName : x)
                        .Where(x => existingNames.Contains(x))
                        .Distinct()
                        .ToArray();
                }

                this.ApplyHideFilterAllTabs();
            });

            this.MarkSettingCommonModified();
            this.SaveConfigsTabs();
        }
    }
}
