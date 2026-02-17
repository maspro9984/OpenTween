#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
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

        /// <summary>タブ名 → (Document, TimelineContentPanel) のマッピング</summary>
        private readonly Dictionary<string, (BaseDocument Document, TimelineContentPanel Content)> tabMap = new();

        /// <summary>タブの順序を管理するリスト</summary>
        private readonly List<string> tabOrder = new();

        private bool suppressEvents;

        public ContextMenuStrip? TabContextMenuStrip { get; set; }

        /// <summary>右クリックされたタブの名前</summary>
        public string? RightClickedTabName { get; private set; }

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
            this.tabbedView.DocumentProperties.AllowPin = false;

            this.tabbedView.DocumentGroupProperties.HeaderLocation = DevExpress.XtraTab.TabHeaderLocation.Top;

            this.tabbedView.DocumentActivated += this.TabbedView_DocumentActivated;
            this.tabbedView.DocumentDeactivated += this.TabbedView_DocumentDeactivated;
            this.MouseUp += this.Container_MouseUp;

            this.documentManager.View = this.tabbedView;

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

            var doc = this.tabbedView.AddDocument(content, tabName) as BaseDocument;
            if (doc == null)
                return;

            doc.Caption = tabName;
            this.tabMap[tabName] = (doc, content);
            this.tabOrder.Add(tabName);
        }

        public void RemoveTab(string tabName)
        {
            if (!this.tabMap.TryGetValue(tabName, out var entry))
                return;

            this.suppressEvents = true;
            try
            {
                this.tabbedView.RemoveDocument(entry.Content);
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
            this.TabDeselected?.Invoke(this, new TabDeselectedEventArgs(prevTabName));
        }

        private void Container_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                // 右クリックされたドキュメントのタブ名を保持
                var activeDoc = this.tabbedView.ActiveDocument;
                this.RightClickedTabName = activeDoc?.Caption;

                if (this.TabContextMenuStrip != null)
                    this.TabContextMenuStrip.Show(Cursor.Position);
            }
            else if (e.Button == MouseButtons.Middle)
            {
                this.TabMouseClick?.Invoke(this, e);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.documentManager.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
