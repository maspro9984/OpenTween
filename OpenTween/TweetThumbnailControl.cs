// OpenTween - Client of Twitter
// Copyright (c) 2012 kim_upsilon (@kim_upsilon) <https://upsilo.net/~upsilon/>
// All rights reserved.
//
// This file is part of OpenTween.
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation; either version 3 of the License, or (at your option)
// any later version.
//
// This program is distributed in the hope that it will be useful, but
// WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
// or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License
// for more details.
//
// You should have received a copy of the GNU General Public License along
// with this program. If not, see <http://www.gnu.org/licenses/>, or write to
// the Free Software Foundation, Inc., 51 Franklin Street - Fifth Floor,
// Boston, MA 02110-1301, USA.

#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenTween.Models;
using OpenTween.Thumbnail;

namespace OpenTween
{
    public partial class TweetThumbnailControl : UserControl
    {
        private readonly MouseWheelMessageFilter filter = new();
        private ThumbnailPreviewForm? previewForm;
        private Timer? hoverTimer;
        private Timer? hideDelayTimer;
        private ThumbnailImageCache? imageCache;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public TweetThumbnail Model { get; } = new();

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ThumbnailImageCache? ImageCache
        {
            get => this.imageCache;
            set
            {
                this.imageCache = value;
                if (this.previewForm != null)
                    this.previewForm.ImageCache = value;
            }
        }

        public TweetThumbnailControl()
        {
            this.InitializeComponent();
            this.filter.Register(this.pictureBox);

            this.Model.PropertyChanged +=
                (s, e) => this.TryInvoke(() => this.Model_PropertyChanged(s, e));

            this.pictureBox.MouseEnter += this.PictureBox_MouseEnter;
            this.pictureBox.MouseLeave += this.PictureBox_MouseLeave;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.components?.Dispose();
                this.filter.Dispose();
                this.hoverTimer?.Dispose();
                this.hideDelayTimer?.Dispose();
                this.previewForm?.Dispose();
            }
            base.Dispose(disposing);
        }

        private void Model_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(TweetThumbnail.ThumbnailAvailable):
                    this.UpdateThumbnailAvailability();
                    break;
                case nameof(TweetThumbnail.SelectedIndex):
                    this.UpdateSelectedIndex();
                    break;
                default:
                    break;
            }
        }

        private void UpdateThumbnailAvailability()
        {
            if (this.Model.ThumbnailAvailable)
            {
                this.UpdateSelectedIndex();

                var hasMultipleThumbnails = this.Model.Thumbnails.Length > 1;
                this.scrollBar.Enabled = hasMultipleThumbnails;
                this.scrollBar.Visible = hasMultipleThumbnails;

                if (hasMultipleThumbnails)
                {
                    this.scrollBar.Value = 0;
                    this.scrollBar.Maximum = this.Model.Thumbnails.Length - 1;
                }
            }
            else
            {
                this.pictureBox.Image = null;
                this.pictureBox.AccessibleDescription = "";
                this.toolTip.SetToolTip(this.pictureBox, "");
                this.scrollBar.Visible = false;
            }
        }

        private void UpdateSelectedIndex()
        {
            if (!this.Model.ThumbnailAvailable)
                return;

            this.scrollBar.Value = this.Model.SelectedIndex;

            var thumbnail = this.Model.CurrentThumbnail;
            this.pictureBox.PlayableMark = thumbnail.IsPlayable;
            this.pictureBox.AccessibleDescription = thumbnail.TooltipText;
            this.toolTip.SetToolTip(this.pictureBox, thumbnail.TooltipText);
            _ = this.pictureBox.SetImageFromTask(this.Model.LoadSelectedThumbnail);

            // プレビューフォームが表示中なら画像を切り替える
            this.UpdatePreviewIfVisible();
        }

        private async void UpdatePreviewIfVisible()
        {
            if (this.previewForm == null || !this.previewForm.Visible)
                return;

            if (!this.Model.ThumbnailAvailable)
                return;

            var thumbnail = this.Model.CurrentThumbnail;
            this.previewForm.InvalidateCache();
            await this.previewForm.ShowPreview(thumbnail, this.previewForm.Location);
        }

        public async Task OpenImageInBrowser()
        {
            if (!this.Model.ThumbnailAvailable)
                return;

            var thumbnail = this.Model.CurrentThumbnail;
            var mediaUrl = thumbnail.FullSizeImageUrl ?? thumbnail.MediaPageUrl;
            await MyCommon.OpenInBrowserAsync(this, mediaUrl);
        }

        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified);
            OTBaseForm.ScaleChildControl(this.scrollBar, factor);
        }

        private void ScrollBar_ValueChanged(object sender, EventArgs e)
            => this.Model.SelectedIndex = this.scrollBar.Value;

        private void PictureBox_MouseWheel(object sender, MouseEventArgs e)
        {
            if (e.Delta > 0)
                this.Model.ScrollUp();
            else
                this.Model.ScrollDown();
        }

        private async void PictureBox_DoubleClick(object sender, EventArgs e)
            => await this.OpenImageInBrowser();

        private async void SearchSimilarImageMenuItem_Click(object sender, EventArgs e)
        {
            var searchUri = this.Model.GetImageSearchUriGoogle();
            await MyCommon.OpenInBrowserAsync(this, searchUri);
        }

        private async void SearchImageSauceNaoMenuItem_Click(object sender, EventArgs e)
        {
            var searchUri = this.Model.GetImageSearchUriSauceNao();
            await MyCommon.OpenInBrowserAsync(this, searchUri);
        }

        private async void OpenMenuItem_Click(object sender, EventArgs e)
            => await this.OpenImageInBrowser();

        private void CopyUrlMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                var thumb = this.Model.CurrentThumbnail;
                Clipboard.SetText(thumb.FullSizeImageUrl ?? thumb.MediaPageUrl);
            }
            catch (ExternalException ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void CopyToClipboardMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                if (this.pictureBox.Image is { } memoryImage)
                    Clipboard.SetImage(memoryImage.Image);
            }
            catch (ExternalException ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void PictureBox_MouseEnter(object? sender, EventArgs e)
        {
            if (!this.Model.ThumbnailAvailable)
                return;

            // 非表示遅延タイマーをキャンセル（プレビューから戻ってきた場合）
            this.hideDelayTimer?.Stop();

            // 既存タイマーを停止してから新しいタイマーを開始
            this.hoverTimer?.Stop();
            this.hoverTimer?.Dispose();
            this.hoverTimer = new Timer { Interval = 300 };
            this.hoverTimer.Tick += this.HoverTimer_Tick;
            this.hoverTimer.Start();
        }

        private void PictureBox_MouseLeave(object? sender, EventArgs e)
        {
            this.hoverTimer?.Stop();

            // プレビューフォームへの移動を許可するため、少し遅延してから非表示にする
            this.hideDelayTimer?.Stop();
            this.hideDelayTimer?.Dispose();
            this.hideDelayTimer = new Timer { Interval = 100 };
            this.hideDelayTimer.Tick += this.HideDelayTimer_Tick;
            this.hideDelayTimer.Start();
        }

        private void HideDelayTimer_Tick(object? sender, EventArgs e)
        {
            this.hideDelayTimer?.Stop();

            // マウスがプレビューフォーム上にある場合は閉じない
            if (this.previewForm != null && this.previewForm.IsMouseOver)
                return;

            // マウスがサムネイル pictureBox 上にある場合も閉じない
            if (this.pictureBox.ClientRectangle.Contains(this.pictureBox.PointToClient(Cursor.Position)))
                return;

            this.previewForm?.HidePreview();
        }

        private void PreviewForm_PreviewHidden(object? sender, EventArgs e)
        {
            // プレビューフォームが自身のMouseLeaveで閉じられた場合のコールバック
            // （特に追加処理は不要だが、将来拡張用に残す）
        }

        private async void HoverTimer_Tick(object? sender, EventArgs e)
        {
            this.hoverTimer?.Stop();

            if (!this.Model.ThumbnailAvailable)
                return;

            if (this.previewForm == null || this.previewForm.IsDisposed)
            {
                this.previewForm = new ThumbnailPreviewForm();
                this.previewForm.ImageCache = this.imageCache;
                this.previewForm.PreviewHidden += this.PreviewForm_PreviewHidden;
            }

            var thumbnail = this.Model.CurrentThumbnail;
            var cursorPos = Cursor.Position;

            await this.previewForm.ShowPreview(thumbnail, cursorPos);
        }
    }
}
