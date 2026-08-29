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
    public class DocumentManagerTabContainer : UserControl, IMessageFilter
    {
        private const int WM_RBUTTONUP = 0x0205;
        private readonly DockManager dockManager;

        /// <summary>タブ名 → (DockPanel, TimelineContentPanel) のマッピング</summary>
        private readonly Dictionary<string, (DockPanel Panel, TimelineContentPanel Content)> tabMap = new();

        /// <summary>タブの順序を管理するリスト</summary>
        private readonly List<string> tabOrder = new();

        /// <summary>DockPanel 名 → コンテンツコントロール（レイアウト復元後の再配置用）</summary>
        private readonly Dictionary<string, Control> detailPanelContents = new();

        /// <summary>タブ名 → コンテンツコントロール（レイアウト復元後の再配置用）</summary>
        private readonly Dictionary<string, Control> tabPanelContents = new();

        /// <summary>タブ名 → 未読表示状態（不要な再描画を避けるため変化時のみ Appearance を更新する）</summary>
        private readonly Dictionary<string, bool> tabUnreadStates = new();

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
            this.dockManager.PopupMenuShowing += this.DockManager_PopupMenuShowing;

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
                this.tabUnreadStates.Remove(tabName);
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

            // 状態が変化していない場合は Appearance を触らない（タブヘッダーの再描画を避ける）
            if (this.tabUnreadStates.TryGetValue(tabName, out var currentState) && currentState == hasUnread)
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

                this.tabUnreadStates[tabName] = hasUnread;
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

            if (this.tabUnreadStates.TryGetValue(oldName, out var unreadState))
            {
                this.tabUnreadStates.Remove(oldName);
                this.tabUnreadStates[newName] = unreadState;
            }

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
            {
                // タブコンテナに紐付けられていない孤立パネル（レイアウト XML の残骸等）は
                // アクティブ化のタイミングで自動削除する
                if (newPanel.Name.StartsWith("TimelineTab_", StringComparison.Ordinal) && !newPanel.Tabbed)
                    this.BeginInvoke(new Action(() => this.RemoveOrphanedPanelSafe(newPanel)));

                return;
            }

            // TabSelecting ハンドラー内で CurrentTab / CurrentTabName が参照される場合に
            // 正しい（新しい）タブ名が返るよう、先に currentActiveTabName を更新する
            var oldActiveTabName = this.currentActiveTabName;
            this.currentActiveTabName = tabName;

            var selectingArgs = new TabSelectingEventArgs(tabName);
            this.TabSelecting?.Invoke(this, selectingArgs);
            if (selectingArgs.Cancel)
            {
                this.currentActiveTabName = oldActiveTabName;
                return;
            }

            this.SelectedTabChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DockManager_PopupMenuShowing(object sender, PopupMenuShowingEventArgs e)
        {
            // DevExpress 標準のポップアップメニューをキャンセル
            // 独自コンテキストメニューの表示は PreFilterMessage (IMessageFilter) が担当する
            e.Cancel = true;
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
            if (!File.Exists(filePath))
                return;

            try
            {
                this.suppressEvents = true;

                // コンテンツコントロールを退避（RestoreLayoutFromXml がパネルを
                // 再生成する際にコントロールが破棄されるのを防止する）
                this.DetachDetailPanelContents();
                this.DetachTabPanelContents();

                this.dockManager.RestoreLayoutFromXml(filePath);

                // 復元されたパネルにコンテンツコントロールを再配置
                this.ReattachDetailPanelContents();
                this.ReattachTabPanelContents();
            }
            catch (Exception)
            {
                // レイアウトファイルの形式が異なる場合（旧形式など）は無視
            }
            finally
            {
                this.suppressEvents = false;
            }

            // try/catch の外で実行し、例外による中断の影響を受けないようにする。
            // また suppressEvents リセット後に実行することで DevExpress の処理が完了した状態で動作する。

            // レイアウト保存時には存在したが現在対応するタブがない孤立パネルを削除
            // （RelatedTweetsなど非永続タブが保存されていた場合に空パネルとして残るのを防ぐ）
            this.RemoveOrphanedTabPanels();

            // timelineTabContainer 参照を更新
            this.UpdateTimelineTabContainerReference();
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
        /// レイアウト復元後、tabMap に登録されていないタイムライン DockPanel を削除する。
        /// dockManager.Panels はネストしたパネルを返さない場合があるため、
        /// RootPanels の再帰探索を主体とし、dockManager.Panels で浮動パネルを補完する。
        /// </summary>
        private void RemoveOrphanedTabPanels()
        {
            var knownPanels = new HashSet<DockPanel>(this.tabMap.Values.Select(e => e.Panel));
            var orphans = new List<DockPanel>();

            // RootPanels を起点に全パネルを再帰探索（タブグループ内のネストしたパネルをカバー）
            foreach (DockPanel rootPanel in this.dockManager.RootPanels)
                this.CollectOrphanedPanelsRecursive(rootPanel, knownPanels, orphans);

            // dockManager.Panels でフローティングパネル等を補完
            // （RootPanels 配下に含まれないパネルが存在する場合の安全策）
            foreach (DockPanel panel in this.dockManager.Panels)
            {
                if (!orphans.Contains(panel) && this.IsOrphanedLeafPanel(panel, knownPanels))
                    orphans.Add(panel);
            }

            foreach (var orphan in orphans)
                this.RemoveOrphanedPanelSafe(orphan);
        }

        private void CollectOrphanedPanelsRecursive(DockPanel panel, HashSet<DockPanel> knownPanels, List<DockPanel> orphans)
        {
            if (!orphans.Contains(panel) && this.IsOrphanedLeafPanel(panel, knownPanels))
                orphans.Add(panel);

            for (var i = 0; i < panel.Count; i++)
                this.CollectOrphanedPanelsRecursive(panel[i], knownPanels, orphans);
        }

        /// <summary>
        /// パネルが孤立したリーフパネル（削除すべき不要パネル）かどうかを判定する。
        /// 主判定は名前プレフィックス（XML復元後も保持される場合）、
        /// 補完判定はコントロールコンテナが空であること（コンテンツが再配置されなかった場合）。
        /// </summary>
        private bool IsOrphanedLeafPanel(DockPanel panel, HashSet<DockPanel> knownPanels)
        {
            // タブコンテナ（Tabbed=true）はリーフパネルでないため対象外
            if (panel.Tabbed)
                return false;

            // 有効なタイムラインパネル（tabMap 登録済み）は対象外
            if (knownPanels.Contains(panel))
                return false;

            // 有効な詳細パネル（detailPanelContents 登録済み）は対象外
            if (this.detailPanelContents.ContainsKey(panel.Name) || this.detailPanelContents.ContainsKey(panel.Text))
                return false;

            // 主判定: 名前プレフィックスで識別（AddTab で付与した "TimelineTab_" が保持されている場合）
            if (panel.Name.StartsWith("TimelineTab_", StringComparison.Ordinal))
                return true;

            // 補完判定: XML 復元後に名前が変わった場合のフォールバック。
            // ReattachTabPanelContents / ReattachDetailPanelContents の後、
            // 有効なパネルにはコンテンツが配置されるため Controls.Count > 0 になる。
            // コンテンツが空のリーフパネルは孤立パネルとみなす。
            if (panel.ControlContainer != null && panel.ControlContainer.Controls.Count == 0)
                return true;

            return false;
        }

        /// <summary>
        /// 指定パネル（またはそのアクティブ子）が孤立タイムラインパネルであれば返す。
        /// tabMap に存在しない TimelineTab_ パネルを孤立パネルと判定する。
        /// </summary>
        private DockPanel? FindOrphanedTimelinePanel(DockPanel panel)
        {
            // 直接パネルが孤立タイムラインパネルか確認（タブコンテナ自体は除外）
            if (panel.Name.StartsWith("TimelineTab_", StringComparison.Ordinal) && !panel.Tabbed)
            {
                if (!this.tabMap.Values.Any(e => e.Panel == panel))
                    return panel;
            }

            // タブコンテナの場合、アクティブな子が孤立パネルかチェック
            if (panel.Tabbed && panel.ActiveChild != null)
            {
                var child = panel.ActiveChild;
                if (child.Name.StartsWith("TimelineTab_", StringComparison.Ordinal) && !child.Tabbed &&
                    !this.tabMap.Values.Any(e => e.Panel == child))
                    return child;
            }

            return null;
        }

        /// <summary>
        /// 孤立パネルを安全に削除する。既に削除済みの場合は例外を無視する。
        /// </summary>
        private void RemoveOrphanedPanelSafe(DockPanel panel)
        {
            try
            {
                this.dockManager.RemovePanel(panel);
            }
            catch (Exception)
            {
            }
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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Application.AddMessageFilter(this);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            Application.RemoveMessageFilter(this);
            base.OnHandleDestroyed(e);
        }

        /// <summary>
        /// タブストリップ領域の右クリックを捕捉し、独自コンテキストメニューを表示する。
        /// DevExpress の PopupMenuShowing はタブストリップでは発火しないため
        /// メッセージフィルターで WM_RBUTTONUP を直接インターセプトする。
        /// </summary>
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_RBUTTONUP)
                return false;

            if (this.IsDisposed || !this.IsHandleCreated)
                return false;

            var cursorPos = Cursor.Position;
            var localPos = this.PointToClient(cursorPos);
            if (!this.ClientRectangle.Contains(localPos))
                return false;

            // タブ名を特定: まずクリック位置のパネルを試みる
            string? tabName = null;
            var panel = this.dockManager.GetDockPanelAtPos(cursorPos);
            if (panel != null)
            {
                tabName = this.FindTabNameByPanel(panel);
                if (tabName == null && panel.Tabbed && panel.ActiveChild != null)
                    tabName = this.FindTabNameByPanel(panel.ActiveChild);
            }

            // タブコンテナの全子パネルを走査して孤立パネルを削除する。
            // GetDockPanelAtPos はコンテナを返すため、ActiveChild（最後に左クリックされたタブ）
            // でなく別の孤立タブを右クリックした場合でも全子を走査することで確実に検出できる。
            if (panel != null && panel.Tabbed)
            {
                var knownPanels = new HashSet<DockPanel>(this.tabMap.Values.Select(e => e.Panel));
                for (var i = 0; i < panel.Count; i++)
                {
                    var child = panel[i];
                    if (child.Name.StartsWith("TimelineTab_", StringComparison.Ordinal) && !child.Tabbed && !knownPanels.Contains(child))
                    {
                        var orphan = child;
                        this.BeginInvoke(new Action(() => this.RemoveOrphanedPanelSafe(orphan)));
                    }
                }
            }

            // 直接の孤立パネル（コンテナでない場合）はコンテキストメニューなしで削除
            if (tabName == null && panel != null)
            {
                var directOrphan = this.FindOrphanedTimelinePanel(panel);
                if (directOrphan != null)
                {
                    this.BeginInvoke(new Action(() => this.RemoveOrphanedPanelSafe(directOrphan)));
                    return false;
                }
            }

            // 孤立パネルでない場合は現在アクティブなタブへフォールバック
            if (tabName == null)
                tabName = this.currentActiveTabName;

            if (tabName == null)
                return false;

            this.RightClickedTabName = tabName;
            var showPoint = cursorPos;
            this.BeginInvoke(new Action(() => this.TabContextMenuStrip?.Show(showPoint)));

            // false を返して DevExpress にもメッセージを渡す（タブ選択動作などを維持）
            // PopupMenuShowing ハンドラーで DevExpress 標準ポップアップはキャンセルされる
            return false;
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
