#nullable enable

using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace OpenTween
{
    public class LinkPreviewForm : Form
    {
        private readonly WebView2 webView;
        private readonly Panel toolBar;
        private readonly Label urlLabel;
        private readonly Button openBrowserButton;
        private readonly Timer mouseCheckTimer;
        private string? currentUrl;
        private string? pendingUrl;
        private bool webViewInitialized;
        private bool mouseEnteredOnce;
        private DateTime? mouseLeftTime;

        public bool IsMouseOver { get; private set; }

        /// <summary>マウスが一度でもプレビューフォーム上に入ったかどうか</summary>
        public bool HasMouseEnteredOnce => this.mouseEnteredOnce;

        /// <summary>マウスがプレビュー外に出てから閉じるまでの遅延 (ミリ秒)</summary>
        public int MouseLeaveDelayMs { get; set; } = 300;

        public string? CurrentUrl => this.currentUrl;

        public event EventHandler? PreviewHidden;

        public LinkPreviewForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.TopMost = true;
            this.BackColor = SystemColors.Control;

            // ツールバー
            this.toolBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = SystemColors.Control,
                Padding = new Padding(4, 2, 4, 2),
            };

            this.openBrowserButton = new Button
            {
                Text = "ブラウザで開く",
                Dock = DockStyle.Right,
                Width = 110,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
            };
            this.openBrowserButton.Click += this.OpenBrowserButton_Click;

            this.urlLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = SystemColors.GrayText,
            };

            this.toolBar.Controls.Add(this.urlLabel);
            this.toolBar.Controls.Add(this.openBrowserButton);

            // WebView2
            this.webView = new WebView2
            {
                Dock = DockStyle.Fill,
            };
            this.webView.CoreWebView2InitializationCompleted += this.WebView_CoreWebView2InitializationCompleted;
            this.webView.NavigationCompleted += this.WebView_NavigationCompleted;

            this.Controls.Add(this.webView);
            this.Controls.Add(this.toolBar);

            // WebView2 はWinFormsのMouseイベントを発火しないため、
            // ポーリングタイマーでカーソル位置を監視する
            this.mouseCheckTimer = new Timer { Interval = 200 };
            this.mouseCheckTimer.Tick += this.MouseCheckTimer_Tick;

            this.SetStyle(ControlStyles.Selectable, true);

            _ = this.InitializeWebViewAsync();
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "OpenTween",
                        "WebView2"
                    )
                );
                await this.webView.EnsureCoreWebView2Async(env);
            }
            catch (Exception)
            {
                // WebView2 ランタイムが未インストールの場合など
            }
        }

        private void WebView_CoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
                return;

            this.webViewInitialized = true;

            // User-Agent を Chrome に偽装
            this.webView.CoreWebView2.Settings.UserAgent =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

            // ステータスバー（左下のリンクURL表示）を無効化（非表示後の描画ゴミ対策）
            this.webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

            // プリロード中（非表示）は音声をミュートしておく
            this.webView.CoreWebView2.IsMuted = true;

            // 新しいウィンドウを開かないようにする
            this.webView.CoreWebView2.NewWindowRequested += (s, args) =>
            {
                args.Handled = true;
                this.webView.CoreWebView2.Navigate(args.Uri);
            };

            // URL変更の追跡
            this.webView.CoreWebView2.SourceChanged += (s, args) =>
            {
                this.currentUrl = this.webView.CoreWebView2.Source;
                this.urlLabel.Text = this.webView.CoreWebView2.Source;
            };

            // 待機中のURLがあれば遷移
            if (this.pendingUrl != null)
            {
                this.NavigateTo(this.pendingUrl);
                this.pendingUrl = null;
            }
        }

        private void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (this.webView.CoreWebView2 != null)
            {
                this.currentUrl = this.webView.CoreWebView2.Source;
                this.urlLabel.Text = this.webView.CoreWebView2.Source;
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // WS_EX_TOOLWINDOW: タスクバーに表示しない
                cp.ExStyle |= 0x00000080;
                return cp;
            }
        }

        public void Navigate(string url)
            => this.NavigateTo(url);

        public void ShowAt(Point screenPos)
        {
            // 表示時にミュートを解除する
            if (this.webViewInitialized && this.webView.CoreWebView2 != null)
                this.webView.CoreWebView2.IsMuted = false;

            this.AdjustSizeAndPosition(screenPos);
            this.IsMouseOver = true;
            this.mouseEnteredOnce = false;
            this.mouseLeftTime = null;
            this.mouseCheckTimer.Start();
            this.Show();
        }

        public void ShowPreview(string url, Point position)
        {
            if (url == this.currentUrl && this.Visible)
                return;

            this.currentUrl = url;
            this.urlLabel.Text = url;
            this.NavigateTo(url);

            // 表示時にミュートを解除する
            if (this.webViewInitialized && this.webView.CoreWebView2 != null)
                this.webView.CoreWebView2.IsMuted = false;

            this.AdjustSizeAndPosition(position);
            this.IsMouseOver = true;
            this.mouseEnteredOnce = false;
            this.mouseLeftTime = null;
            this.mouseCheckTimer.Start();
            this.Show();
        }

        private void NavigateTo(string url)
        {
            if (this.webViewInitialized && this.webView.CoreWebView2 != null)
            {
                this.webView.CoreWebView2.Navigate(url);
            }
            else
            {
                this.pendingUrl = url;
            }
        }

        private void AdjustSizeAndPosition(Point cursorPosition)
        {
            var screen = Screen.FromPoint(cursorPosition).WorkingArea;

            var width = Math.Min((int)(screen.Width * 0.715), 1144);
            var height = Math.Min((int)(screen.Height * 0.9), 1080);

            this.Size = new Size(width, height);

            // カーソル位置の右上に表示
            var x = cursorPosition.X + 16;
            var y = cursorPosition.Y - this.Height - 8;

            if (x + this.Width > screen.Right)
                x = cursorPosition.X - this.Width - 16;

            if (y < screen.Top)
                y = cursorPosition.Y + 24;

            if (y + this.Height > screen.Bottom)
                y = screen.Bottom - this.Height;

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
            if (this.Bounds.Contains(cursorPos))
            {
                this.IsMouseOver = true;
                this.mouseEnteredOnce = true;
                this.mouseLeftTime = null;
            }
            else
            {
                this.IsMouseOver = false;

                // マウスが一度もポップアップに入っていなければまだ閉じない
                if (!this.mouseEnteredOnce)
                    return;

                // マウスが離れた時刻を記録し、遅延時間が経過するまで閉じない
                if (this.mouseLeftTime == null)
                {
                    this.mouseLeftTime = DateTime.UtcNow;
                    return;
                }

                if ((DateTime.UtcNow - this.mouseLeftTime.Value).TotalMilliseconds < this.MouseLeaveDelayMs)
                    return;

                this.mouseLeftTime = null;
                this.mouseCheckTimer.Stop();
                this.PreviewHidden?.Invoke(this, EventArgs.Empty);
                this.HidePreview();
            }
        }

        private async void OpenBrowserButton_Click(object? sender, EventArgs e)
        {
            if (this.currentUrl != null)
                await MyCommon.OpenInBrowserAsync(this, this.currentUrl);
        }

        public void HidePreview()
        {
            this.mouseCheckTimer.Stop();
            this.IsMouseOver = false;

            if (this.webViewInitialized && this.webView.CoreWebView2 != null)
                this.webView.CoreWebView2.Navigate("about:blank");

            this.currentUrl = null;
            this.Hide();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.mouseCheckTimer.Dispose();
                this.webView.Dispose();
                this.openBrowserButton.Dispose();
                this.urlLabel.Dispose();
                this.toolBar.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
