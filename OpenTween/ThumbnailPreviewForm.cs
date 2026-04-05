#nullable enable

using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenTween.Connection;
using OpenTween.Models;
using OpenTween.Thumbnail;

namespace OpenTween
{
    public class ThumbnailPreviewForm : Form
    {
        private readonly OTPictureBox pictureBox;
        private readonly MouseWheelMessageFilter wheelFilter = new();
        private CancellationTokenSource? loadCts;
        private string? lastLoadedUrl;
        private Size originalImageSize;
        private double zoomScale = 1.0;

        /// <summary>マウスがプレビューフォーム上にあるかどうか</summary>
        public bool IsMouseOver { get; private set; }

        /// <summary>フルサイズ画像のキャッシュ</summary>
        public ThumbnailImageCache? ImageCache { get; set; }

        /// <summary>プレビューを閉じるべきときに発火するイベント</summary>
        public event EventHandler? PreviewHidden;

        public ThumbnailPreviewForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.TopMost = true;
            this.BackColor = Color.Black;
            this.Padding = new Padding(1);

            this.pictureBox = new OTPictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
            };
            this.Controls.Add(this.pictureBox);
            this.wheelFilter.Register(this.pictureBox);

            this.pictureBox.MouseWheel += this.PictureBox_MouseWheel;
            this.pictureBox.MouseEnter += (s, e) => this.IsMouseOver = true;
            this.pictureBox.MouseLeave += this.PreviewForm_MouseLeave;
            this.MouseEnter += (s, e) => this.IsMouseOver = true;
            this.MouseLeave += this.PreviewForm_MouseLeave;

            // フォーカスを奪わないようにする
            this.SetStyle(ControlStyles.Selectable, false);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW: フォーカスを奪わず、タスクバーに表示しない
                cp.ExStyle |= 0x08000000 | 0x00000080;
                return cp;
            }
        }

        public async Task ShowPreview(ThumbnailInfo thumbnail, Point position)
        {
            var imageUrl = thumbnail.FullSizeImageUrl ?? thumbnail.ThumbnailImageUrl;
            if (imageUrl == null)
                return;

            // 同じ画像なら再読み込みしない
            if (imageUrl == this.lastLoadedUrl && this.Visible)
                return;

            // キャッシュチェック（ヒットした場合は即座に表示）
            if (this.ImageCache?.TryGet(imageUrl) is { } cachedImage)
            {
                this.loadCts?.Cancel();
                var cloned = cachedImage.Clone();
                this.pictureBox.Image = cloned;
                this.lastLoadedUrl = imageUrl;
                this.originalImageSize = cloned.Image.Size;
                this.zoomScale = 1.0;
                this.AdjustSizeAndPosition(cloned.Image.Size, position);
                this.Show();
                return;
            }

            this.loadCts?.Cancel();
            this.loadCts = new CancellationTokenSource();
            var token = this.loadCts.Token;

            try
            {
                var image = await Task.Run(
                    () => this.LoadImageAsync(imageUrl, token),
                    token
                );

                if (token.IsCancellationRequested)
                    return;

                // キャッシュに保存して、表示用にクローンを使用
                MemoryImage displayImage;
                if (this.ImageCache != null)
                {
                    this.ImageCache.Store(imageUrl, image);
                    displayImage = image.Clone();
                }
                else
                {
                    displayImage = image;
                }

                this.pictureBox.Image = displayImage;
                this.lastLoadedUrl = imageUrl;
                this.originalImageSize = displayImage.Image.Size;
                this.zoomScale = 1.0;

                this.AdjustSizeAndPosition(displayImage.Image.Size, position);
                this.Show();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                // 読み込み失敗時は表示しない
            }
        }

        private async Task<MemoryImage> LoadImageAsync(string imageUrl, CancellationToken token)
        {
            var loader = new SimpleThumbnailLoader(imageUrl);
            return await loader.Load(Networking.Http, token).ConfigureAwait(false);
        }

        private void AdjustSizeAndPosition(Size imageSize, Point cursorPosition)
        {
            var screen = Screen.FromPoint(cursorPosition).WorkingArea;

            // 高DPI 環境では画面の物理ピクセル数が大きいため、
            // DPI スケールファクターまでは拡大を許可する
            float dpiScale;
            using (var g = this.CreateGraphics())
            {
                dpiScale = g.DpiX / 96f;
            }

            // 画面サイズの60%を上限
            var maxWidth = (int)(screen.Width * 0.6);
            var maxHeight = (int)(screen.Height * 0.6);

            // アスペクト比を維持してリサイズ
            var scale = Math.Min(
                (double)maxWidth / imageSize.Width,
                (double)maxHeight / imageSize.Height
            );

            // DPI スケール倍までは拡大を許可する（等倍では高DPI環境で小さすぎる）
            scale = Math.Min(scale, dpiScale);

            // 小さすぎる場合は最低サイズを確保
            var width = Math.Max((int)(imageSize.Width * scale), 200);
            var height = Math.Max((int)(imageSize.Height * scale), 150);

            this.Size = new Size(width + this.Padding.Horizontal, height + this.Padding.Vertical);

            // カーソル位置の右上に表示、画面外に出ないよう調整
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

        private void ApplyZoom()
        {
            var screen = Screen.FromControl(this).WorkingArea;

            var width = Math.Max((int)(this.originalImageSize.Width * this.zoomScale), 100);
            var height = Math.Max((int)(this.originalImageSize.Height * this.zoomScale), 75);

            // 画面サイズを超えないようにする
            width = Math.Min(width, screen.Width - 20);
            height = Math.Min(height, screen.Height - 20);

            // フォームの中心を維持してリサイズ
            var centerX = this.Left + this.Width / 2;
            var centerY = this.Top + this.Height / 2;

            this.Size = new Size(width + this.Padding.Horizontal, height + this.Padding.Vertical);

            var newX = centerX - this.Width / 2;
            var newY = centerY - this.Height / 2;

            // 画面外にはみ出さないよう調整
            newX = Math.Max(screen.Left, Math.Min(newX, screen.Right - this.Width));
            newY = Math.Max(screen.Top, Math.Min(newY, screen.Bottom - this.Height));

            this.Location = new Point(newX, newY);
        }

        private void PictureBox_MouseWheel(object? sender, MouseEventArgs e)
        {
            if (this.originalImageSize.IsEmpty)
                return;

            if (e.Delta > 0)
                this.zoomScale *= 1.2;
            else
                this.zoomScale /= 1.2;

            // ズーム範囲を制限（10%～500%）
            this.zoomScale = Math.Max(0.1, Math.Min(this.zoomScale, 5.0));

            this.ApplyZoom();
        }

        private void PreviewForm_MouseLeave(object? sender, EventArgs e)
        {
            // マウスがフォーム内の子コントロールに移動した場合は閉じない
            var cursorPos = Cursor.Position;
            if (this.Bounds.Contains(cursorPos))
                return;

            this.IsMouseOver = false;
            this.PreviewHidden?.Invoke(this, EventArgs.Empty);
            this.Hide();
        }

        public void HidePreview()
        {
            this.loadCts?.Cancel();
            this.IsMouseOver = false;
            this.Hide();
        }

        /// <summary>表示中の画像URLをクリアし、次回 ShowPreview で再読み込みさせる</summary>
        public void InvalidateCache()
        {
            this.lastLoadedUrl = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.loadCts?.Cancel();
                this.loadCts?.Dispose();
                this.wheelFilter.Dispose();
                this.pictureBox.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
