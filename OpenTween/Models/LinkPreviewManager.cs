#nullable enable

using System;
using System.Drawing;

namespace OpenTween.Models
{
    public sealed class LinkPreviewManager : IDisposable
    {
        private readonly LinkPreviewForm form = new();

        private int mouseLeaveDelayMs = 500;

        /// <summary>マウスがプレビュー外に出てから閉じるまでの遅延 (ミリ秒)</summary>
        public int MouseLeaveDelayMs
        {
            get => this.mouseLeaveDelayMs;
            set
            {
                this.mouseLeaveDelayMs = value;
                this.form.MouseLeaveDelayMs = value;
            }
        }

        public void ShowPreview(string url, Point screenPos)
        {
            if (this.form.Visible && this.form.CurrentUrl == url)
                return;

            this.form.ShowPreview(url, screenPos);
        }

        public bool IsAnyFormMouseOver
            => this.form.IsMouseOver || (this.form.Visible && !this.form.HasMouseEnteredOnce);

        public void HideAll()
        {
            if (this.form.Visible)
                this.form.HidePreview();
        }

        public void Dispose()
            => this.form.Dispose();
    }
}
