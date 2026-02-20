#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace OpenTween.Models
{
    public sealed class ThumbnailImageCache : IDisposable
    {
        private const int MaxSize = 20;

        private readonly Dictionary<string, MemoryImage> cache = new();
        private readonly Queue<string> insertOrder = new();

        public MemoryImage? TryGet(string url)
        {
            if (this.cache.TryGetValue(url, out var img))
            {
                Debug.WriteLine($"[ThumbnailCache] キャッシュHIT: {url}");
                return img;
            }

            return null;
        }

        public void Store(string url, MemoryImage image)
        {
            if (this.cache.ContainsKey(url))
            {
                image.Dispose();
                return;
            }

            while (this.cache.Count >= MaxSize)
            {
                var oldest = this.insertOrder.Dequeue();
                if (this.cache.TryGetValue(oldest, out var old))
                {
                    this.cache.Remove(oldest);
                    old.Dispose();
                }
            }

            this.cache[url] = image;
            this.insertOrder.Enqueue(url);
            Debug.WriteLine($"[ThumbnailCache] キャッシュ追加 ({this.cache.Count}件): {url}");
        }

        public void Dispose()
        {
            foreach (var img in this.cache.Values)
                img.Dispose();
            this.cache.Clear();
        }
    }
}
