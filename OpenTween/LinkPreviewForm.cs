#nullable enable

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace OpenTween
{
    public class LinkPreviewForm : Form
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int VK_LBUTTON = 0x01;
        private const int VK_RBUTTON = 0x02;
        private const int VK_MBUTTON = 0x04;

        private readonly WebView2 webView;
        private readonly Panel toolBar;
        private readonly Label urlLabel;
        private readonly Button openBrowserButton;
        private readonly Timer mouseCheckTimer;
        private string? currentUrl;
        private string? pendingUrl;
        private bool webViewInitialized;
        private bool webViewInitStarted;
        private bool mouseEnteredOnce;
        private DateTime? mouseLeftTime;
        private DateTime showedAt;

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
        }

        /// <summary>仮想マシン上で動作しているかを BIOS 情報から判定する</summary>
        private static bool IsVirtualizedEnvironment()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                if (key == null)
                    return false;

                var manufacturer = key.GetValue("SystemManufacturer") as string ?? "";
                var productName = key.GetValue("SystemProductName") as string ?? "";
                var combined = manufacturer + " " + productName;

                return combined.IndexOf("Parallels", StringComparison.OrdinalIgnoreCase) >= 0
                    || combined.IndexOf("VMware", StringComparison.OrdinalIgnoreCase) >= 0
                    || combined.IndexOf("VirtualBox", StringComparison.OrdinalIgnoreCase) >= 0
                    || combined.IndexOf("QEMU", StringComparison.OrdinalIgnoreCase) >= 0
                    || combined.IndexOf("Virtual Machine", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>WebView2 の初期化を開始する（フォーム表示時に呼ばれる）</summary>
        private async Task EnsureWebViewInitializedAsync()
        {
            if (this.webViewInitStarted)
                return;

            this.webViewInitStarted = true;

            try
            {
                var options = new CoreWebView2EnvironmentOptions();

                // 仮想化環境（Parallels Desktop 等）では GPU レンダリングが動作しないため
                // ソフトウェアレンダリングにフォールバックする。
                // 実機では GPU レンダリングを使用する（swiftshader は CPU 負荷が高いため）
                if (IsVirtualizedEnvironment())
                    options.AdditionalBrowserArguments = "--disable-gpu --disable-gpu-compositing --disable-gpu-sandbox --use-gl=swiftshader";
                var env = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "OpenTween",
                        "WebView2"
                    ),
                    options: options
                );
                await this.webView.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                // WebView2 ランタイムが未インストールの場合など — 原因が分かるよう表示する
                var errorLabel = new Label
                {
                    Text = $"WebView2 初期化失敗: {ex.GetType().Name}: {ex.Message}",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Color.Red,
                    BackColor = SystemColors.Control,
                };
                this.Controls.Remove(this.webView);
                this.Controls.Add(errorLabel);
            }
        }

        private void WebView_CoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                var errorLabel = new Label
                {
                    Text = $"WebView2 初期化失敗: {e.InitializationException?.GetType().Name}: {e.InitializationException?.Message}",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Color.Red,
                    BackColor = SystemColors.Control,
                };
                this.Controls.Remove(this.webView);
                this.Controls.Add(errorLabel);
                return;
            }

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
            this.showedAt = DateTime.UtcNow;
            this.mouseCheckTimer.Start();
            this.Show();
        }

        public void ShowPreview(string url, Point position)
        {
            if (url == this.currentUrl && this.Visible)
                return;

            this.currentUrl = url;
            this.urlLabel.Text = url;

            this.AdjustSizeAndPosition(position);
            this.IsMouseOver = true;
            this.mouseEnteredOnce = false;
            this.mouseLeftTime = null;
            this.showedAt = DateTime.UtcNow;
            this.mouseCheckTimer.Start();

            // フォームを先に表示してから WebView2 を初期化・ナビゲーションする
            // (非表示状態での初期化/ナビゲーションは仮想化環境で描画が動作しない)
            this.Show();

            // 表示時にミュートを解除する
            if (this.webViewInitialized && this.webView.CoreWebView2 != null)
            {
                this.webView.CoreWebView2.IsMuted = false;
                this.webView.CoreWebView2.Navigate(url);
            }
            else
            {
                this.pendingUrl = url;
                _ = this.EnsureWebViewInitializedAsync();
            }
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

            // 仮想化環境（Parallel Desktop 等）で WorkingArea が不正な値を返す場合のフォールバック
            if (screen.Width < 400 || screen.Height < 300)
                screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);

            var width = Math.Max(400, Math.Min((int)(screen.Width * 0.715), 1144));
            var height = Math.Max(300, Math.Min((int)(screen.Height * 0.9), 1080));

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

            // 画面範囲内に収める（仮想化環境での座標ずれ対策）
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
            // ただし表示直後（500ms以内）はボタン状態を確認しない（仮想化環境での誤閉じ対策）
            if (!this.Bounds.Contains(cursorPos) &&
                (DateTime.UtcNow - this.showedAt).TotalMilliseconds >= 500)
            {
                var lButton = GetAsyncKeyState(VK_LBUTTON);
                var rButton = GetAsyncKeyState(VK_RBUTTON);
                var mButton = GetAsyncKeyState(VK_MBUTTON);
                if ((lButton & 0x8000) != 0 || (rButton & 0x8000) != 0 || (mButton & 0x8000) != 0)
                {
                    this.mouseLeftTime = null;
                    this.mouseCheckTimer.Stop();
                    this.PreviewHidden?.Invoke(this, EventArgs.Empty);
                    this.HidePreview();
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
