#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraBars.Docking;
using DevExpress.XtraBars.Docking2010;
using DevExpress.XtraBars.Docking2010.Views;
using DevExpress.XtraBars.Docking2010.Views.Tabbed;
using OpenTween.OpenTweenCustomControl;

namespace OpenTween.Controls
{
    public class DocumentManagerTabContainer : UserControl
    {
        private readonly DocumentManager documentManager;
        private readonly TabbedView tabbedView;
        private readonly DockManager dockManager;

        /// <summary>タブ名 → (Document, TimelineContentPanel) のマッピング</summary>
        private readonly Dictionary<string, (BaseDocument Document, TimelineContentPanel Content)> tabMap = new();

        /// <summary>タブの順序を管理するリスト</summary>
        private readonly List<string> tabOrder = new();

        private bool suppressEvents;

        public ContextMenuStrip? TabContextMenuStrip { get; set; }

        /// <summary>右クリックされたタブの名前</summary>
        public string? RightClickedTabName { get; set; }

        public int SelectedIndex
        {
            get
            {
                var activeDoc = this.tabbedView.ActiveDocument;
                if (activeDoc == null)
                    return -1;

                var tabName = activeDoc.Caption;
                return this.tabOrder.IndexOf(tabName);
            }

            set
            {
                if (value < 0 || value >= this.tabOrder.Count)
                    return;

                var tabName = this.tabOrder[value];
                if (this.tabMap.TryGetValue(tabName, out var entry))
                    this.tabbedView.ActivateDocument(entry.Content);
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

            this.documentManager = new DocumentManager
            {
                ContainerControl = this,
            };

            this.tabbedView = new TabbedView();

            this.tabbedView.DocumentProperties.AllowClose = false;
            this.tabbedView.DocumentProperties.AllowFloat = true;
            this.tabbedView.DocumentProperties.AllowDock = true;
            this.tabbedView.DocumentProperties.AllowDockFill = true;
            this.tabbedView.DocumentProperties.AllowPin = false;

            this.tabbedView.EnableFreeLayoutMode = DevExpress.Utils.DefaultBoolean.True;
            this.tabbedView.EnableStickySplitters = DevExpress.Utils.DefaultBoolean.True;
            this.tabbedView.FloatingDocumentContainer = FloatingDocumentContainer.DocumentsHost;

            this.tabbedView.DocumentGroupProperties.HeaderLocation = DevExpress.XtraTab.TabHeaderLocation.Top;

            this.tabbedView.DocumentActivated += this.TabbedView_DocumentActivated;
            this.tabbedView.DocumentDeactivated += this.TabbedView_DocumentDeactivated;
            this.tabbedView.PopupMenuShowing += this.TabbedView_PopupMenuShowing;
            this.MouseUp += this.Container_MouseUp;

            this.documentManager.View = this.tabbedView;

            this.dockManager = new DockManager();
            this.dockManager.Form = this;

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

            // DevExpress はコントロール名でドキュメントを識別するため、一意な名前を設定
            content.Name = tabName;

            var doc = this.tabbedView.AddDocument(content, tabName) as BaseDocument;
            if (doc == null)
                return;

            doc.Caption = tabName;
            this.tabMap[tabName] = (doc, content);
            this.tabOrder.Add(tabName);
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
            panel.ControlContainer.Controls.Add(content);
        }

        public void RemoveTab(string tabName)
        {
            if (!this.tabMap.TryGetValue(tabName, out var entry))
                return;

            this.suppressEvents = true;
            try
            {
                // AllowClose=false のため、一時的に許可してから閉じる
                if (entry.Document is Document doc)
                    doc.Properties.AllowClose = DevExpress.Utils.DefaultBoolean.True;

                this.tabbedView.Controller.Close(entry.Document);
                this.tabMap.Remove(tabName);
                this.tabOrder.Remove(tabName);
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
                this.tabbedView.ActivateDocument(entry.Content);
        }

        public void MoveTab(string targetTabName, int newIndex)
        {
            if (!this.tabOrder.Contains(targetTabName))
                return;

            this.tabOrder.Remove(targetTabName);

            if (newIndex > this.tabOrder.Count)
                newIndex = this.tabOrder.Count;

            this.tabOrder.Insert(newIndex, targetTabName);
        }

        public void SetTabUnreadState(string tabName, bool hasUnread)
        {
            if (this.IsDisposed)
                return;

            if (!this.tabMap.TryGetValue(tabName, out var entry))
                return;

            var doc = entry.Document as Document;
            if (doc == null)
                return;

            try
            {
                if (hasUnread)
                    doc.Appearance.Header.ForeColor = Color.Red;
                else
                    doc.Appearance.Header.Reset();
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

            entry.Document.Caption = newName;
            entry.Content.TabName = newName;
            entry.Content.Name = newName;
            this.tabMap.Remove(oldName);
            this.tabMap[newName] = entry;

            var index = this.tabOrder.IndexOf(oldName);
            if (index >= 0)
                this.tabOrder[index] = newName;
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
            => this.tabbedView.DocumentGroupProperties.HeaderLocation = location;

        private void TabbedView_DocumentActivated(object sender, DocumentEventArgs e)
        {
            if (this.suppressEvents)
                return;

            var tabName = e.Document?.Caption;
            if (tabName == null)
                return;

            // DetailPanel など tabMap に存在しないドキュメントのアクティブ化は無視
            if (!this.tabMap.ContainsKey(tabName))
                return;

            var selectingArgs = new TabSelectingEventArgs(tabName);
            this.TabSelecting?.Invoke(this, selectingArgs);
            if (selectingArgs.Cancel)
                return;

            this.SelectedTabChanged?.Invoke(this, EventArgs.Empty);
        }

        private void TabbedView_DocumentDeactivated(object sender, DocumentEventArgs e)
        {
            if (this.suppressEvents)
                return;

            var prevTabName = e.Document?.Caption;
            if (prevTabName != null && !this.tabMap.ContainsKey(prevTabName))
                return;

            this.TabDeselected?.Invoke(this, new TabDeselectedEventArgs(prevTabName));
        }

        private void TabbedView_PopupMenuShowing(object sender, DevExpress.XtraBars.Docking2010.Views.PopupMenuShowingEventArgs e)
        {
            // DevExpress 標準のポップアップメニューを抑制し、独自のコンテキストメニューを表示
            e.Cancel = true;

            // PopupMenuShowing 発火時点で DevExpress は右クリック対象のドキュメントをアクティブにしている
            this.RightClickedTabName = this.tabbedView.ActiveDocument?.Caption;

            if (this.TabContextMenuStrip != null)
                this.TabContextMenuStrip.Show(Cursor.Position);
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
                // TabbedView レイアウト保存
                if (this.tabbedView.DocumentGroups.Count <= 1)
                {
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                    return;
                }

                this.tabbedView.SaveLayoutToXml(filePath);
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
                this.tabbedView.RestoreLayoutFromXml(filePath);
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.dockManager.Dispose();
                this.documentManager.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
