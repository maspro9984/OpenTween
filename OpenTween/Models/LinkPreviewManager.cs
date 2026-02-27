#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;

namespace OpenTween.Models
{
    public sealed class LinkPreviewManager : IDisposable
    {
        private const int PoolSize = 10;

        private readonly List<LinkPreviewForm> pool;
        private readonly Dictionary<string, int> urlToFormIndex = new();

        // 未使用スロットのキュー（起動直後のみ使用、pool が埋まったら空になる）
        private readonly Queue<int> freeSlots;

        // LRU キュー: (URL, バージョン番号) のペアで管理。
        // ホバーでURLが「最新」に昇格すると新しいバージョンのエントリが追加され、
        // 古いエントリはバージョン不一致で追い出し時にスキップされる。
        private readonly Queue<(string Url, int Version)> loadOrder = new();
        private readonly Dictionary<string, int> urlQueueVersion = new();

        public LinkPreviewManager()
        {
            this.pool = new List<LinkPreviewForm>(PoolSize);
            this.freeSlots = new Queue<int>(PoolSize);
            for (var i = 0; i < PoolSize; i++)
            {
                this.pool.Add(new LinkPreviewForm());
                this.freeSlots.Enqueue(i);
            }
        }

        public void Preload(IEnumerable<string> urls)
        {
            foreach (var url in urls)
            {
                if (this.urlToFormIndex.ContainsKey(url))
                {
                    Debug.WriteLine($"[LinkPreview] Preload SKIP(キャッシュ済み): {url}");
                    continue;
                }

                var idx = this.FindFreeOrEvictSlot();
                Debug.WriteLine($"[LinkPreview] Preload ロード開始 slot[{idx}]: {url}");
                this.pool[idx].Navigate(url);
                this.urlToFormIndex[url] = idx;
                this.EnqueueUrl(url);
            }
        }

        public void ShowPreview(string url, Point screenPos)
        {
            // 他の表示中フォームはページを維持したまま非表示（HideOnly でキャッシュ保持）
            foreach (var f in this.pool)
            {
                if (f.Visible && f.CurrentUrl != url)
                    f.HideOnly();
            }

            var cachedUrls = string.Join(", ", this.urlToFormIndex.Keys);
            if (this.urlToFormIndex.TryGetValue(url, out var idx))
            {
                Debug.WriteLine($"[LinkPreview] キャッシュHIT: {url}");
                Debug.WriteLine($"[LinkPreview] キャッシュ済みURL({this.urlToFormIndex.Count}件): {cachedUrls}");

                // ホバーされたURLをLRUで最新に昇格
                this.EnqueueUrl(url);
                this.pool[idx].ShowAt(screenPos);
            }
            else
            {
                Debug.WriteLine($"[LinkPreview] キャッシュMISS: {url}");
                Debug.WriteLine($"[LinkPreview] キャッシュ済みURL({this.urlToFormIndex.Count}件): {cachedUrls}");
                var newIdx = this.FindFreeOrEvictSlot();
                this.urlToFormIndex[url] = newIdx;
                this.EnqueueUrl(url);
                this.pool[newIdx].ShowPreview(url, screenPos);
            }
        }

        public bool IsAnyFormMouseOver
            => this.pool.Exists(f => f.IsMouseOver);

        public void HideAll()
        {
            foreach (var f in this.pool)
            {
                if (f.Visible)
                    f.HideOnly();
            }
        }

        public void Dispose()
        {
            foreach (var f in this.pool)
                f.Dispose();
        }

        private void EnqueueUrl(string url)
        {
            var version = (this.urlQueueVersion.TryGetValue(url, out var existing) ? existing : 0) + 1;
            this.urlQueueVersion[url] = version;
            this.loadOrder.Enqueue((url, version));
        }

        private int FindFreeOrEvictSlot()
        {
            // 未使用スロットがあれば優先使用（CurrentUrl に依存しない）
            if (this.freeSlots.Count > 0)
                return this.freeSlots.Dequeue();

            // 全スロット使用中 → LRUキューから最古の有効エントリを追い出す
            while (this.loadOrder.Count > 0)
            {
                var (url, version) = this.loadOrder.Dequeue();

                // バージョンが古い（昇格済み）エントリはスキップ
                if (!this.urlQueueVersion.TryGetValue(url, out var currentVersion) || version != currentVersion)
                    continue;

                if (this.urlToFormIndex.TryGetValue(url, out var idx))
                {
                    this.urlToFormIndex.Remove(url);
                    this.urlQueueVersion.Remove(url);
                    return idx;
                }
            }

            return 0;
        }
    }
}
