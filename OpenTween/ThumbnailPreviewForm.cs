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

        /// <summary>サムネイルの拡大プレビューを表示する</summary>
        /// <param name="thumbnail">表示対象のサムネイル</param>
        /// <param name="position">表示位置の基準となるカーソル位置</param>
        /// <param name="thumbnailImageTask">
        /// 読み込み済み（または読み込み中）のサムネイル画像。
        /// フルサイズ画像の取得を待つ間、または取得に失敗した場合の表示に使用する
        /// </param>
        public async Task ShowPreview(ThumbnailInfo thumbnail, Point position, Task<MemoryImage>? thumbnailImageTask = null)
        {
            var imageUrl = thumbnail.FullSizeImageUrl ?? thumbnail.ThumbnailImageUrl;
            if (imageUrl == null && thumbnailImageTask == null)
                return;

            var previewKey = imageUrl ?? thumbnail.MediaPageUrl;

            // 同じ画像なら再読み込みしない
            if (previewKey == this.lastLoadedUrl && this.Visible)
                return;

            this.loadCts?.Cancel();
            var cts = new CancellationTokenSource();
            this.loadCts = cts;
            var token = cts.Token;

            this.pictureBox.LoadingProgress = null;

            // キャッシュチェック（ヒットした場合は即座に表示）
            if (imageUrl != null && this.ImageCache?.TryGet(imageUrl) is { } cachedImage)
            {
                this.SetPreviewImage(cachedImage.Clone(), position);
                this.lastLoadedUrl = previewKey;
                return;
            }

            // サムネイル画像と同じ URL であれば改めてダウンロードする必要はない
            var needsFullSizeLoad = imageUrl != null &&
                (thumbnailImageTask == null || thumbnail.Loader != null || imageUrl != thumbnail.ThumbnailImageUrl);

            // フルサイズ画像の読み込み状況をプレビュー上に表示する。
            // Progress<T> の通知は UI スレッドに非同期で届くため、読み込み終了後に届いた通知は無視する
            var fullSizeLoading = true;
            var progress = new Progress<DownloadProgress>(x =>
            {
                if (fullSizeLoading && !token.IsCancellationRequested)
                    this.pictureBox.LoadingProgress = x;
            });

            var fullSizeTask = needsFullSizeLoad
                ? Task.Run(() => this.LoadImageAsync(imageUrl!, progress, token), token)
                : null;

            // フルサイズ画像の取得には時間が掛かる（または失敗する）場合があるため、
            // 先に読み込み済みのサムネイル画像を拡大表示しておく
            var placeholderShown = false;
            if (thumbnailImageTask != null)
            {
                try
                {
                    var thumbnailImage = await thumbnailImageTask;

                    if (token.IsCancellationRequested)
                    {
                        DisposeWhenCompleted(fullSizeTask);
                        return;
                    }

                    // フルサイズ画像が先に取得できている場合はそちらを優先する
                    if (fullSizeTask == null || fullSizeTask.Status != TaskStatus.RanToCompletion)
                    {
                        this.SetPreviewImage(thumbnailImage.Clone(), position);
                        this.lastLoadedUrl = previewKey;
                        placeholderShown = true;
                    }
                }
                catch (Exception)
                {
                    // サムネイル画像が使えない場合はフルサイズ画像の取得結果のみで表示する
                }
            }

            if (fullSizeTask == null)
                return;

            try
            {
                MemoryImage image;
                try
                {
                    image = await fullSizeTask;
                }
                finally
                {
                    fullSizeLoading = false;
                    if (!token.IsCancellationRequested)
                        this.pictureBox.LoadingProgress = null;
                }

                if (token.IsCancellationRequested)
                {
                    image.Dispose();
                    return;
                }

                // キャッシュに保存して、表示用にクローンを使用
                MemoryImage displayImage;
                if (this.ImageCache != null)
                {
                    displayImage = image.Clone();
                    this.ImageCache.Store(imageUrl!, image);
                }
                else
                {
                    displayImage = image;
                }

                if (placeholderShown)
                    this.ReplacePreviewImage(displayImage);
                else
                    this.SetPreviewImage(displayImage, position);

                this.lastLoadedUrl = previewKey;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                // 読み込み失敗時はサムネイル画像の拡大表示のままとする
            }
        }

        private static void DisposeWhenCompleted(Task<MemoryImage>? task)
        {
            if (task == null)
                return;

            _ = AsyncExceptionBoundary.IgnoreExceptionAndDispose(task);
        }

        /// <summary>プレビュー画像を設定し、画像サイズに合わせてフォームを配置して表示する</summary>
        private void SetPreviewImage(MemoryImage image, Point position)
        {
            var oldImage = this.pictureBox.Image;
            this.pictureBox.Image = image;
            oldImage?.Dispose();

            this.originalImageSize = image.Image.Size;
            this.AdjustSizeAndPosition(image.Image.Size, position);
            this.UpdateZoomScaleFromCurrentSize();

            if (!this.Visible)
                this.Show();
        }

        /// <summary>表示中のフォームの位置・サイズを維持したまま、より高解像度な画像に差し替える</summary>
        private void ReplacePreviewImage(MemoryImage image)
        {
            var oldImage = this.pictureBox.Image;
            this.pictureBox.Image = image;
            oldImage?.Dispose();

            this.originalImageSize = image.Image.Size;
            this.UpdateZoomScaleFromCurrentSize();
        }

        /// <summary>現在の表示サイズを基準にズーム倍率を設定する（ホイール操作時に表示サイズが飛ばないようにする）</summary>
        private void UpdateZoomScaleFromCurrentSize()
        {
            if (this.originalImageSize.Width <= 0)
            {
                this.zoomScale = 1.0;
                return;
            }

            var displayWidth = this.Width - this.Padding.Horizontal;
            this.zoomScale = Math.Max(0.1, Math.Min((double)displayWidth / this.originalImageSize.Width, 5.0));
        }

        private async Task<MemoryImage> LoadImageAsync(string imageUrl, IProgress<DownloadProgress>? progress, CancellationToken token)
        {
            var loader = new SimpleThumbnailLoader(imageUrl);
            return await loader.Load(Networking.Http, progress, token).ConfigureAwait(false);
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
            this.pictureBox.LoadingProgress = null;
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
