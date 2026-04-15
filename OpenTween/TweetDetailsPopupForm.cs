#nullable enable

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OpenTween
{
    /// <summary>
    /// DateTimeLabel ホバー時に発言詳細を大きく表示するポップアップ。
    /// WebBrowser を 1 つ持つだけの軽量な実装で、
    /// LinkPreviewForm と同様のマウス操作挙動（外クリックで閉じる / 離れて一定時間で閉じる）を持つ。
    /// </summary>
    public class TweetDetailsPopupForm : Form
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int VK_LBUTTON = 0x01;
        private const int VK_RBUTTON = 0x02;
        private const int VK_MBUTTON = 0x04;

        private readonly Panel headerPanel;
        private readonly Label nameLabel;
        private readonly Label dateLabel;
        private readonly Label sourceLabel;
        private readonly WebBrowser webBrowser;
        private readonly Timer mouseCheckTimer;
        private bool mouseEnteredOnce;
        private DateTime? mouseLeftTime;
        private DateTime showedAt;

        public bool IsMouseOver { get; private set; }

        /// <summary>マウスが外に出てから閉じるまでの遅延 (ミリ秒)</summary>
        public int MouseLeaveDelayMs { get; set; } = 300;

        public TweetDetailsPopupForm()
        {
            this.FormBorderStyle = FormBorderStyle.FixedToolWindow;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.TopMost = true;
            this.BackColor = SystemColors.Window;
            this.Padding = new Padding(1);

            this.headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 48,
                BackColor = SystemColors.Control,
                Padding = new Padding(8, 4, 8, 4),
            };

            this.nameLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            };

            this.dateLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 18,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = SystemColors.GrayText,
            };

            this.sourceLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 0,
                Visible = false,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = SystemColors.GrayText,
            };

            this.headerPanel.Controls.Add(this.sourceLabel);
            this.headerPanel.Controls.Add(this.dateLabel);
            this.headerPanel.Controls.Add(this.nameLabel);

            this.webBrowser = new WebBrowser
            {
                Dock = DockStyle.Fill,
                AllowWebBrowserDrop = false,
                IsWebBrowserContextMenuEnabled = false,
                WebBrowserShortcutsEnabled = false,
                ScriptErrorsSuppressed = true,
            };

            this.Controls.Add(this.webBrowser);
            this.Controls.Add(this.headerPanel);

            this.mouseCheckTimer = new Timer { Interval = 200 };
            this.mouseCheckTimer.Tick += this.MouseCheckTimer_Tick;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        public void ShowPopup(string html, string name, string date, string source, Point screenPos)
        {
            this.nameLabel.Text = name;
            this.dateLabel.Text = date;
            this.sourceLabel.Text = source;

            this.webBrowser.DocumentText = html;

            this.AdjustSizeAndPosition(screenPos);
            this.IsMouseOver = true;
            this.mouseEnteredOnce = false;
            this.mouseLeftTime = null;
            this.showedAt = DateTime.UtcNow;
            this.mouseCheckTimer.Start();

            if (!this.Visible)
                this.Show();
        }

        private void AdjustSizeAndPosition(Point cursorPosition)
        {
            var screen = Screen.FromPoint(cursorPosition).WorkingArea;

            if (screen.Width < 400 || screen.Height < 300)
                screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);

            var width = Math.Max(400, (int)(screen.Width * 0.5));
            var height = Math.Max(300, (int)(screen.Height * 0.6));

            this.Size = new Size(width, height);

            var x = cursorPosition.X + 16;
            var y = cursorPosition.Y - this.Height - 8;

            if (x + this.Width > screen.Right)
                x = cursorPosition.X - this.Width - 16;

            if (y < screen.Top)
                y = cursorPosition.Y + 24;

            if (y + this.Height > screen.Bottom)
                y = screen.Bottom - this.Height;

            x = Math.Max(screen.Left, Math.Min(x, screen.Right - this.Width));
            y = Math.Max(screen.Top, Math.Min(y, screen.Bottom - this.Height));

            this.Location = new Point(x, y);
        }

        private void MouseCheckTimer_Tick(object? sender, EventArgs e)
        {
            if (!this.Visible)
            {
                this.mouseCheckTimer.Stop();
                return;
            }

            var cursorPos = Cursor.Position;

            // ウィンドウ外でマウスボタンが押されたら閉じる
            if (!this.Bounds.Contains(cursorPos) &&
                (DateTime.UtcNow - this.showedAt).TotalMilliseconds >= 500)
            {
                var lButton = GetAsyncKeyState(VK_LBUTTON);
                var rButton = GetAsyncKeyState(VK_RBUTTON);
                var mButton = GetAsyncKeyState(VK_MBUTTON);
                if ((lButton & 0x8000) != 0 || (rButton & 0x8000) != 0 || (mButton & 0x8000) != 0)
                {
                    this.HidePopup();
                    return;
                }
            }

            if (this.Bounds.Contains(cursorPos))
            {
                this.IsMouseOver = true;
                this.mouseEnteredOnce = true;
                this.mouseLeftTime = null;
            }
            else
            {
                this.IsMouseOver = false;

                if (!this.mouseEnteredOnce)
                    return;

                if (this.mouseLeftTime == null)
                {
                    this.mouseLeftTime = DateTime.UtcNow;
                    return;
                }

                if ((DateTime.UtcNow - this.mouseLeftTime.Value).TotalMilliseconds < this.MouseLeaveDelayMs)
                    return;

                this.HidePopup();
            }
        }

        public void HidePopup()
        {
            this.mouseCheckTimer.Stop();
            this.IsMouseOver = false;
            this.mouseLeftTime = null;
            if (this.Visible)
                this.Hide();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.mouseCheckTimer.Dispose();
                this.webBrowser.Dispose();
                this.nameLabel.Dispose();
                this.dateLabel.Dispose();
                this.sourceLabel.Dispose();
                this.headerPanel.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
