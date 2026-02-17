#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraBars.Docking;
using OpenTween.OpenTweenCustomControl;

namespace OpenTween.Controls
{
    public class DocumentManagerTabContainer : UserControl
    {
        private readonly DockManager dockManager;

        /// <summary>タブ名 → (DockPanel, TimelineContentPanel) のマッピング</summary>
        private readonly Dictionary<string, (DockPanel Panel, TimelineContentPanel Content)> tabMap = new();

        /// <summary>タブの順序を管理するリスト</summary>
        private readonly List<string> tabOrder = new();

        /// <summary>DockPanel 名 → コンテンツコントロール（レイアウト復元後の再配置用）</summary>
        private readonly Dictionary<string, Control> detailPanelContents = new();

        /// <summary>タブ名 → コンテンツコントロール（レイアウト復元後の再配置用）</summary>
        private readonly Dictionary<string, Control> tabPanelContents = new();

        private bool suppressEvents;

        /// <summary>タイムラインタブのコンテナパネル参照</summary>
        private DockPanel? timelineTabContainer;

        /// <summary>現在アクティブなタブ名</summary>
        private string? currentActiveTabName;

        public ContextMenuStrip? TabContextMenuStrip { get; set; }

        /// <summary>右クリックされたタブの名前</summary>
        public string? RightClickedTabName { get; set; }

        public int SelectedIndex
        {
            get
            {
                if (this.currentActiveTabName == null)
                    return -1;

                return this.tabOrder.IndexOf(this.currentActiveTabName);
            }

            set
            {
                if (value < 0 || value >= this.tabOrder.Count)
                    return;

                var tabName = this.tabOrder[value];
                if (this.tabMap.TryGetValue(tabName, out var entry))
                    this.dockManager.ActivePanel = entry.Panel;
            }
        }

        public int TabCount => this.tabOrder.Count;

        public event EventHandler? SelectedTabChanged;

        public event EventHandler<TabSelectingEventArgs>? TabSelecting;

        public event EventHandler<TabDeselectedEventArgs>? TabDeselected;

        public event KeyEventHandler? TabKeyDown;

        public event MouseEventHandler? TabMouseClick;

        public DocumentManagerTabContainer()
        {
            this.Dock = DockStyle.Fill;

            this.dockManager = new DockManager();
            this.dockManager.Form = this;
            this.dockManager.DockingOptions.AllowDockToCenter = DevExpress.Utils.DefaultBoolean.True;

            this.dockManager.ActivePanelChanged += this.DockManager_ActivePanelChanged;

            this.MouseUp += this.Container_MouseUp;
            this.KeyDown += (s, e) => this.TabKeyDown?.Invoke(this, e);
        }

        public TimelineContentPanel? GetContentPanel(string tabName)
        {
            if (this.tabMap.TryGetValue(tabName, out var entry))
                return entry.Content;
            return null;
        }

        public DetailsListView? GetListView(string tabName)
            => this.GetContentPanel(tabName)?.ListView;

        public TimelineContentPanel GetContentPanelAt(int index)
        {
            var tabName = this.tabOrder[index];
            return this.tabMap[tabName].Content;
        }

        public void AddTab(string tabName, TimelineContentPanel content)
        {
            if (this.tabMap.ContainsKey(tabName))
                return;

            content.Name = tabName;
            content.Dock = DockStyle.Fill;

            DockPanel panel;
            if (this.timelineTabContainer == null)
            {
                // 最初のタイムラインタブは中央（Fill）にドッキング
                panel = this.dockManager.AddPanel(DockingStyle.Fill);
                this.timelineTabContainer = panel;
            }
            else
            {
                // 後続のタブは既存コンテナにタブとして追加
                panel = this.dockManager.AddPanel(DockingStyle.Float);
                panel.DockAsTab(this.timelineTabContainer);
            }

            panel.Text = tabName;
            panel.Name = "TimelineTab_" + tabName;
            panel.Options.ShowCloseButton = false;
            panel.ControlContainer.Controls.Add(content);

            this.tabMap[tabName] = (panel, content);
            this.tabPanelContents[tabName] = content;
            this.tabOrder.Add(tabName);

            // DockAsTab 後に ParentPanel がタブコンテナに変わる場合は参照を更新
            if (panel.ParentPanel != null && panel.ParentPanel.Tabbed)
                this.timelineTabContainer = panel.ParentPanel;
        }

        /// <summary>
        /// 閉じることのできないツールパネル（DetailView等）を DockPanel として追加する。
        /// DockPanel 同士はフローティング時にも相互ドッキング可能。
        /// </summary>
        private DockPanel? detailPanelContainer;

        public void AddDetailPanel(string name, Control content)
        {
            content.Name = name;
            content.Dock = DockStyle.Fill;

            DockPanel panel;
            if (this.detailPanelContainer == null)
            {
                // 最初のパネルはフォーム下部にドッキング
                panel = this.dockManager.AddPanel(DockingStyle.Bottom);
                this.detailPanelContainer = panel;
            }
            else
            {
                // 後続のパネルは既存パネルにタブとして追加
                panel = this.detailPanelContainer.AddPanel();
            }

            panel.Text = name;
            panel.Name = name;
            panel.Options.ShowCloseButton = false;
            panel.ControlContainer.ImeMode = System.Windows.Forms.ImeMode.Inherit;
            panel.ControlContainer.Controls.Add(content);

            this.detailPanelContents[name] = content;
        }

        public void RemoveTab(string tabName)
        {
            if (!this.tabMap.TryGetValue(tabName, out var entry))
                return;

            this.suppressEvents = true;
            try
            {
                // コンテンツコントロールを退避してからパネルを削除
                entry.Content.Parent?.Controls.Remove(entry.Content);
                this.dockManager.RemovePanel(entry.Panel);

                this.tabMap.Remove(tabName);
                this.tabPanelContents.Remove(tabName);
                this.tabOrder.Remove(tabName);

                if (this.tabMap.Count == 0)
                    this.timelineTabContainer = null;
            }
            finally
            {
                this.suppressEvents = false;
            }
        }

        public void SelectTab(int index)
        {
            if (index < 0 || index >= this.tabOrder.Count)
                return;

            var tabName = this.tabOrder[index];
            if (this.tabMap.TryGetValue(tabName, out var entry))
                this.dockManager.ActivePanel = entry.Panel;
        }

        public void MoveTab(string targetTabName, int newIndex)
        {
            if (!this.tabOrder.Contains(targetTabName))
                return;

            if (!this.tabMap.TryGetValue(targetTabName, out var entry))
                return;

            this.tabOrder.Remove(targetTabName);

            if (newIndex > this.tabOrder.Count)
                newIndex = this.tabOrder.Count;

            this.tabOrder.Insert(newIndex, targetTabName);

            // DockPanel のタブコンテナ内の位置も変更
            entry.Panel.Index = newIndex;
        }

        public void SetTabUnreadState(string tabName, bool hasUnread)
        {
            if (this.IsDisposed)
                return;

            if (!this.tabMap.TryGetValue(tabName, out var entry))
                return;

            try
            {
                if (hasUnread)
                {
                    entry.Panel.Appearance.ForeColor = Color.Red;
                    entry.Panel.Appearance.Options.UseForeColor = true;
                }
                else
                {
                    entry.Panel.Appearance.Reset();
                }
            }
            catch (NullReferenceException)
            {
                // 終了処理中にDevExpress内部オブジェクトが破棄済みの場合は無視
            }
        }

        public void RenameTab(string oldName, string newName)
        {
            if (!this.tabMap.TryGetValue(oldName, out var entry))
                return;

            entry.Panel.Text = newName;
            entry.Panel.Name = "TimelineTab_" + newName;
            entry.Content.TabName = newName;
            entry.Content.Name = newName;

            this.tabMap.Remove(oldName);
            this.tabMap[newName] = entry;

            this.tabPanelContents.Remove(oldName);
            this.tabPanelContents[newName] = entry.Content;

            var index = this.tabOrder.IndexOf(oldName);
            if (index >= 0)
                this.tabOrder[index] = newName;

            if (this.currentActiveTabName == oldName)
                this.currentActiveTabName = newName;
        }

        public IEnumerable<DetailsListView> GetAllListViews()
            => this.tabOrder
                .Where(name => this.tabMap.ContainsKey(name))
                .Select(name => this.tabMap[name].Content.ListView);

        public IEnumerable<TimelineContentPanel> GetAllContentPanels()
            => this.tabOrder
                .Where(name => this.tabMap.ContainsKey(name))
                .Select(name => this.tabMap[name].Content);

        public void SetTabHeaderLocation(DevExpress.XtraTab.TabHeaderLocation location)
        {
            if (this.timelineTabContainer == null)
                return;

            var tabsPosition = location switch
            {
                DevExpress.XtraTab.TabHeaderLocation.Top => TabsPosition.Top,
                DevExpress.XtraTab.TabHeaderLocation.Bottom => TabsPosition.Bottom,
                DevExpress.XtraTab.TabHeaderLocation.Left => TabsPosition.Left,
                DevExpress.XtraTab.TabHeaderLocation.Right => TabsPosition.Right,
                _ => TabsPosition.Top,
            };

            this.timelineTabContainer.TabsPosition = tabsPosition;
        }

        private void DockManager_ActivePanelChanged(object sender, ActivePanelChangedEventArgs e)
        {
            if (this.suppressEvents)
                return;

            // 旧パネルの非アクティブ化（TabDeselected 相当）
            if (e.OldPanel != null)
            {
                var oldTabName = this.FindTabNameByPanel(e.OldPanel);
                if (oldTabName != null)
                    this.TabDeselected?.Invoke(this, new TabDeselectedEventArgs(oldTabName));
            }

            // 新パネルのアクティブ化（DocumentActivated 相当）
            var newPanel = e.Panel;
            if (newPanel == null)
                return;

            var tabName = this.FindTabNameByPanel(newPanel);

            // タブコンテナ自体がアクティブになった場合は ActiveChild を確認
            if (tabName == null && newPanel.Tabbed && newPanel.ActiveChild != null)
                tabName = this.FindTabNameByPanel(newPanel.ActiveChild);

            if (tabName == null)
                return;

            var selectingArgs = new TabSelectingEventArgs(tabName);
            this.TabSelecting?.Invoke(this, selectingArgs);
            if (selectingArgs.Cancel)
                return;

            this.currentActiveTabName = tabName;
            this.SelectedTabChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// tabMap から DockPanel に対応するタブ名を検索する
        /// </summary>
        private string? FindTabNameByPanel(DockPanel panel)
        {
            foreach (var kvp in this.tabMap)
            {
                if (kvp.Value.Panel == panel)
                    return kvp.Key;
            }
            return null;
        }

        private void Container_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                this.TabMouseClick?.Invoke(this, e);
            }
        }

        public void SaveLayout(string filePath)
        {
            try
            {
                this.dockManager.SaveLayoutToXml(filePath);
            }
            catch (Exception)
            {
            }
        }

        public void RestoreLayout(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return;

                this.suppressEvents = true;

                // コンテンツコントロールを退避（RestoreLayoutFromXml がパネルを
                // 再生成する際にコントロールが破棄されるのを防止する）
                this.DetachDetailPanelContents();
                this.DetachTabPanelContents();

                this.dockManager.RestoreLayoutFromXml(filePath);

                // 復元されたパネルにコンテンツコントロールを再配置
                this.ReattachDetailPanelContents();
                this.ReattachTabPanelContents();

                // timelineTabContainer 参照を更新
                this.UpdateTimelineTabContainerReference();
            }
            catch (Exception)
            {
                // レイアウトファイルの形式が異なる場合（旧形式など）は無視
            }
            finally
            {
                this.suppressEvents = false;
            }
        }

        /// <summary>
        /// DockManager のレイアウト復元前にコンテンツコントロールをパネルから退避する。
        /// RestoreLayoutFromXml はパネルを再生成するため、退避しないとコントロールが破棄される。
        /// </summary>
        private void DetachDetailPanelContents()
        {
            foreach (var content in this.detailPanelContents.Values)
            {
                content.Parent?.Controls.Remove(content);
            }
        }

        /// <summary>
        /// タイムラインタブのコンテンツコントロールをパネルから退避する
        /// </summary>
        private void DetachTabPanelContents()
        {
            foreach (var content in this.tabPanelContents.Values)
            {
                content.Parent?.Controls.Remove(content);
            }
        }

        /// <summary>
        /// DockManager のレイアウト復元後、復元されたパネルにコンテンツコントロールを再配置する
        /// </summary>
        private void ReattachDetailPanelContents()
        {
            var attached = new HashSet<string>();

            // Panels と Name で直接マッチ
            foreach (DockPanel panel in this.dockManager.Panels)
            {
                if (this.TryReattachDetailContent(panel, out var matchedName) && matchedName != null)
                    attached.Add(matchedName);
            }

            // マッチしなかったコンテンツがある場合、RootPanels の子パネルも再帰探索
            foreach (DockPanel rootPanel in this.dockManager.RootPanels)
            {
                this.ReattachDetailContentRecursive(rootPanel, attached);
            }

            // レイアウト復元後にマッチしなかったコンテンツがあれば、新しいパネルを作成して配置
            foreach (var kvp in this.detailPanelContents)
            {
                if (attached.Contains(kvp.Key))
                    continue;

                // コンテンツが既にどこかに配置済みならスキップ
                if (kvp.Value.Parent != null)
                    continue;

                var panel = this.dockManager.AddPanel(DockingStyle.Bottom);
                panel.Text = kvp.Key;
                panel.Name = kvp.Key;
                panel.Options.ShowCloseButton = false;
                panel.ControlContainer.ImeMode = System.Windows.Forms.ImeMode.Inherit;
                kvp.Value.Dock = DockStyle.Fill;
                panel.ControlContainer.Controls.Add(kvp.Value);
                attached.Add(kvp.Key);
            }
        }

        /// <summary>
        /// レイアウト復元後、タイムラインタブのコンテンツを復元されたパネルに再配置する
        /// </summary>
        private void ReattachTabPanelContents()
        {
            var attached = new HashSet<string>();

            // Panels と Name で直接マッチ
            foreach (DockPanel panel in this.dockManager.Panels)
            {
                if (this.TryReattachTabContent(panel, out var matchedName) && matchedName != null)
                    attached.Add(matchedName);
            }

            // RootPanels の子パネルも再帰探索
            foreach (DockPanel rootPanel in this.dockManager.RootPanels)
            {
                this.ReattachTabContentRecursive(rootPanel, attached);
            }

            // マッチしなかったコンテンツは新しいパネルを作成して配置
            foreach (var kvp in this.tabPanelContents)
            {
                if (attached.Contains(kvp.Key))
                    continue;

                if (kvp.Value.Parent != null)
                    continue;

                DockPanel panel;
                if (this.timelineTabContainer == null)
                {
                    panel = this.dockManager.AddPanel(DockingStyle.Fill);
                    this.timelineTabContainer = panel;
                }
                else
                {
                    panel = this.dockManager.AddPanel(DockingStyle.Float);
                    panel.DockAsTab(this.timelineTabContainer);
                }

                panel.Text = kvp.Key;
                panel.Name = "TimelineTab_" + kvp.Key;
                panel.Options.ShowCloseButton = false;
                kvp.Value.Dock = DockStyle.Fill;
                panel.ControlContainer.Controls.Add(kvp.Value);

                // tabMap のパネル参照を更新
                if (this.tabMap.ContainsKey(kvp.Key))
                    this.tabMap[kvp.Key] = (panel, (TimelineContentPanel)kvp.Value);

                if (panel.ParentPanel != null && panel.ParentPanel.Tabbed)
                    this.timelineTabContainer = panel.ParentPanel;

                attached.Add(kvp.Key);
            }
        }

        private void ReattachDetailContentRecursive(DockPanel panel, HashSet<string> attached)
        {
            if (this.TryReattachDetailContent(panel, out var matchedName) && matchedName != null)
                attached.Add(matchedName);

            for (var i = 0; i < panel.Count; i++)
                this.ReattachDetailContentRecursive(panel[i], attached);
        }

        private void ReattachTabContentRecursive(DockPanel panel, HashSet<string> attached)
        {
            if (this.TryReattachTabContent(panel, out var matchedName) && matchedName != null)
                attached.Add(matchedName);

            for (var i = 0; i < panel.Count; i++)
                this.ReattachTabContentRecursive(panel[i], attached);
        }

        private bool TryReattachDetailContent(DockPanel panel, out string? matchedName)
        {
            matchedName = null;

            if (panel.ControlContainer == null)
                return false;

            // Name または Text でマッチ
            string contentKey;
            if (this.detailPanelContents.ContainsKey(panel.Name))
                contentKey = panel.Name;
            else if (this.detailPanelContents.ContainsKey(panel.Text))
                contentKey = panel.Text;
            else
                return false;

            var content = this.detailPanelContents[contentKey];
            matchedName = contentKey;

            // 既に配置済みなら再配置しない
            if (content.Parent == panel.ControlContainer)
                return true;

            content.Dock = DockStyle.Fill;
            panel.ControlContainer.ImeMode = System.Windows.Forms.ImeMode.Inherit;
            panel.ControlContainer.Controls.Add(content);

            return true;
        }

        private bool TryReattachTabContent(DockPanel panel, out string? matchedName)
        {
            matchedName = null;

            if (panel.ControlContainer == null)
                return false;

            // "TimelineTab_" プレフィックス付き Name または Text でマッチ
            string? contentKey = null;
            foreach (var kvp in this.tabPanelContents)
            {
                if (panel.Name == "TimelineTab_" + kvp.Key || panel.Text == kvp.Key)
                {
                    contentKey = kvp.Key;
                    break;
                }
            }

            if (contentKey == null)
                return false;

            var content = this.tabPanelContents[contentKey];
            matchedName = contentKey;

            // 既に配置済みなら再配置しない
            if (content.Parent == panel.ControlContainer)
            {
                // tabMap のパネル参照を更新
                if (this.tabMap.ContainsKey(contentKey))
                    this.tabMap[contentKey] = (panel, (TimelineContentPanel)content);
                return true;
            }

            content.Dock = DockStyle.Fill;
            panel.ControlContainer.Controls.Add(content);

            // tabMap のパネル参照を更新
            if (this.tabMap.ContainsKey(contentKey))
                this.tabMap[contentKey] = (panel, (TimelineContentPanel)content);

            return true;
        }

        /// <summary>
        /// レイアウト復元後、timelineTabContainer 参照を更新する
        /// </summary>
        private void UpdateTimelineTabContainerReference()
        {
            this.timelineTabContainer = null;

            if (this.tabMap.Count == 0)
                return;

            // 最初のタイムラインパネルの親をコンテナとして設定
            var firstEntry = this.tabMap.Values.First();
            if (firstEntry.Panel.ParentPanel != null && firstEntry.Panel.ParentPanel.Tabbed)
                this.timelineTabContainer = firstEntry.Panel.ParentPanel;
            else
                this.timelineTabContainer = firstEntry.Panel;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.dockManager.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
