#nullable enable

using System.Windows.Forms;
using OpenTween.OpenTweenCustomControl;

namespace OpenTween.Controls
{
    public partial class TimelineContentPanel : UserControl
    {
        public DetailsListView ListView { get; }

        public Control? HeaderPanel { get; }

        public string TabName { get; set; }

        public TimelineContentPanel(DetailsListView listView, Control? headerPanel, string tabName)
        {
            this.InitializeComponent();

            this.ListView = listView;
            this.HeaderPanel = headerPanel;
            this.TabName = tabName;
            this.Dock = DockStyle.Fill;

            this.SuspendLayout();

            listView.Dock = DockStyle.Fill;
            this.Controls.Add(listView);

            if (headerPanel != null)
            {
                headerPanel.Dock = DockStyle.Top;
                this.Controls.Add(headerPanel);
            }

            this.ResumeLayout(false);
        }
    }
}
