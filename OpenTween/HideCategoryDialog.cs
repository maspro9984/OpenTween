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
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenTween.Models;

namespace OpenTween
{
    /// <summary>
    /// 非表示カテゴリ (まとめて表示を隠すユーザーのグループ) を編集するダイアログ
    /// </summary>
    public class HideCategoryDialog : Form
    {
        private readonly List<EditingCategory> editingCategories;
        private readonly ListBox categoryListBox;
        private readonly TextBox nameTextBox;
        private readonly TextBox screenNamesTextBox;
        private readonly Button removeButton;
        private bool suppressEvents;

        /// <summary>編集後のカテゴリ</summary>
        public List<HideCategory> Categories { get; private set; } = new();

        /// <summary>名前が変更されたカテゴリ (変更前の名前 → 変更後の名前)</summary>
        public Dictionary<string, string> RenamedCategories { get; private set; } = new();

        public HideCategoryDialog(IEnumerable<HideCategory> categories)
        {
            this.editingCategories = categories
                .Select(x => new EditingCategory(x.Name, x.Name, x.ScreenNames.ToList()))
                .ToList();

            this.Text = "非表示カテゴリの編集";
            this.AutoScaleMode = AutoScaleMode.Font;
            this.Font = SystemFonts.MessageBoxFont;
            this.ClientSize = new Size(560, 400);
            this.MinimumSize = new Size(480, 320);
            this.StartPosition = FormStartPosition.CenterParent;
            this.ShowInTaskbar = false;
            this.MinimizeBox = false;
            this.MaximizeBox = false;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(8),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // 左側: カテゴリ一覧と追加・削除ボタン
            var leftPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            leftPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            leftPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            leftPanel.Controls.Add(new Label { Text = "カテゴリ", AutoSize = true }, 0, 0);

            this.categoryListBox = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            this.categoryListBox.SelectedIndexChanged += (s, e) => this.LoadSelectedCategory();
            leftPanel.Controls.Add(this.categoryListBox, 0, 1);

            var leftButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            var addButton = new Button { Text = "追加", AutoSize = true };
            addButton.Click += (s, e) => this.AddCategory();
            this.removeButton = new Button { Text = "削除", AutoSize = true };
            this.removeButton.Click += (s, e) => this.RemoveCategory();
            leftButtons.Controls.Add(addButton);
            leftButtons.Controls.Add(this.removeButton);
            leftPanel.Controls.Add(leftButtons, 0, 2);

            // 右側: カテゴリ名とユーザー一覧
            var rightPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            rightPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rightPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rightPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            rightPanel.Controls.Add(new Label { Text = "カテゴリ名", AutoSize = true }, 0, 0);

            this.nameTextBox = new TextBox { Dock = DockStyle.Fill };
            this.nameTextBox.TextChanged += (s, e) => this.UpdateSelectedCategoryName();
            rightPanel.Controls.Add(this.nameTextBox, 0, 1);

            var screenNamesLabel = new Label
            {
                Text = "隠すユーザーの @ID (1行に1人)",
                AutoSize = true,
                Margin = new Padding(3, 8, 3, 3),
            };
            rightPanel.Controls.Add(screenNamesLabel, 0, 2);

            this.screenNamesTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
            };
            this.screenNamesTextBox.TextChanged += (s, e) => this.UpdateSelectedCategoryScreenNames();
            rightPanel.Controls.Add(this.screenNamesTextBox, 0, 3);

            // 下部: OK / キャンセル
            var bottomButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
            };
            var cancelButton = new Button { Text = "キャンセル", AutoSize = true, DialogResult = DialogResult.Cancel };
            var okButton = new Button { Text = "OK", AutoSize = true };
            okButton.Click += (s, e) => this.Accept();
            bottomButtons.Controls.Add(cancelButton);
            bottomButtons.Controls.Add(okButton);

            layout.Controls.Add(leftPanel, 0, 0);
            layout.Controls.Add(rightPanel, 1, 0);
            layout.Controls.Add(bottomButtons, 0, 1);
            layout.SetColumnSpan(bottomButtons, 2);

            this.Controls.Add(layout);
            this.AcceptButton = okButton;
            this.CancelButton = cancelButton;

            this.RefreshCategoryList(selectIndex: 0);
        }

        private EditingCategory? SelectedCategory
        {
            get
            {
                var index = this.categoryListBox.SelectedIndex;
                return index >= 0 && index < this.editingCategories.Count ? this.editingCategories[index] : null;
            }
        }

        private void RefreshCategoryList(int selectIndex)
        {
            this.suppressEvents = true;
            try
            {
                this.categoryListBox.BeginUpdate();
                this.categoryListBox.Items.Clear();
                foreach (var category in this.editingCategories)
                    this.categoryListBox.Items.Add(FormatListItem(category));
                this.categoryListBox.EndUpdate();

                if (this.editingCategories.Count > 0)
                    this.categoryListBox.SelectedIndex = Math.Max(0, Math.Min(selectIndex, this.editingCategories.Count - 1));
            }
            finally
            {
                this.suppressEvents = false;
            }

            this.LoadSelectedCategory();
        }

        private static string FormatListItem(EditingCategory category)
            => $"{category.Name} ({category.ScreenNames.Count}人)";

        private void LoadSelectedCategory()
        {
            if (this.suppressEvents)
                return;

            var category = this.SelectedCategory;

            this.suppressEvents = true;
            try
            {
                this.nameTextBox.Text = category?.Name ?? "";
                this.screenNamesTextBox.Text = category != null ? string.Join(Environment.NewLine, category.ScreenNames) : "";
            }
            finally
            {
                this.suppressEvents = false;
            }

            this.nameTextBox.Enabled = category != null;
            this.screenNamesTextBox.Enabled = category != null;
            this.removeButton.Enabled = category != null;
        }

        private void UpdateSelectedCategoryName()
        {
            if (this.suppressEvents || this.SelectedCategory is not { } category)
                return;

            category.Name = this.nameTextBox.Text.Trim();
            this.UpdateSelectedListItem(category);
        }

        private void UpdateSelectedCategoryScreenNames()
        {
            if (this.suppressEvents || this.SelectedCategory is not { } category)
                return;

            category.ScreenNames = this.screenNamesTextBox.Lines
                .Select(PostHideFilter.NormalizeScreenName)
                .Where(x => x.Length > 0)
                .ToList();
            this.UpdateSelectedListItem(category);
        }

        private void UpdateSelectedListItem(EditingCategory category)
        {
            this.suppressEvents = true;
            try
            {
                this.categoryListBox.Items[this.categoryListBox.SelectedIndex] = FormatListItem(category);
            }
            finally
            {
                this.suppressEvents = false;
            }
        }

        private void AddCategory()
        {
            var baseName = "新しいカテゴリ";
            var name = baseName;
            for (var i = 2; this.editingCategories.Any(x => x.Name == name); i++)
                name = $"{baseName} {i}";

            this.editingCategories.Add(new EditingCategory(null, name, new List<string>()));
            this.RefreshCategoryList(selectIndex: this.editingCategories.Count - 1);

            this.nameTextBox.Focus();
            this.nameTextBox.SelectAll();
        }

        private void RemoveCategory()
        {
            var index = this.categoryListBox.SelectedIndex;
            if (index < 0)
                return;

            var category = this.editingCategories[index];
            var result = MessageBox.Show(
                this,
                $"カテゴリ「{category.Name}」を削除しますか？",
                this.Text,
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);

            if (result != DialogResult.OK)
                return;

            this.editingCategories.RemoveAt(index);
            this.RefreshCategoryList(selectIndex: index);
        }

        private void Accept()
        {
            if (this.editingCategories.Any(x => x.Name.Length == 0))
            {
                MessageBox.Show(this, "カテゴリ名が空欄のカテゴリがあります。", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var duplicatedName = this.editingCategories
                .GroupBy(x => x.Name)
                .FirstOrDefault(x => x.Count() > 1)?.Key;

            if (duplicatedName != null)
            {
                MessageBox.Show(this, $"カテゴリ名「{duplicatedName}」が重複しています。", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            this.Categories = this.editingCategories
                .Select(x => new HideCategory
                {
                    Name = x.Name,
                    ScreenNames = x.ScreenNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                })
                .ToList();

            this.RenamedCategories = this.editingCategories
                .Where(x => x.OriginalName != null && x.OriginalName != x.Name)
                .ToDictionary(x => x.OriginalName!, x => x.Name);

            this.DialogResult = DialogResult.OK;
        }

        private class EditingCategory
        {
            public string? OriginalName { get; }

            public string Name { get; set; }

            public List<string> ScreenNames { get; set; }

            public EditingCategory(string? originalName, string name, List<string> screenNames)
            {
                this.OriginalName = originalName;
                this.Name = name;
                this.ScreenNames = screenNames;
            }
        }
    }
}
