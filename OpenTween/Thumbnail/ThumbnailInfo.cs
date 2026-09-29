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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenTween.Connection;

namespace OpenTween.Thumbnail
{
    public record ThumbnailInfo(
        string MediaPageUrl,
        string? ThumbnailImageUrl
    )
    {
        /// <summary>サムネイルとして表示するメディアが掲載されている URL</summary>
        /// <remarks>
        /// 例えば Youtube のサムネイルの場合、動画そのものの URL ではなく
        /// https://www.youtube.com/watch?v=****** 形式の URL が含まれる
        /// </remarks>
        public string MediaPageUrl { get; init; } = MediaPageUrl;

        /// <summary>サムネイルとして表示する画像の URL</summary>
        /// <remarks>
        /// ここに含まれる URL は直接画像として表示可能である必要がある
        /// </remarks>
        public string? ThumbnailImageUrl { get; init; } = ThumbnailImageUrl;

        /// <summary>最も高解像度な画像の URL</summary>
        /// <remarks>
        /// サムネイルとしては不適だが、より高解像度な画像を表示する場面に使用できる
        /// URL があればここに含まれる
        /// </remarks>
        public string? FullSizeImageUrl { get; init; }

        /// <summary>ツールチップとして表示するテキスト</summary>
        /// <remarks>
        /// サムネイル画像にマウスオーバーした際に表示されるテキスト
        /// </remarks>
        public string TooltipText { get; init; } = "";

        /// <summary>
        /// 対象となるメディアが動画や音声など再生可能なものであるか否か
        /// </summary>
        public bool IsPlayable { get; init; }

        public IThumbnailLoader? Loader { get; init; }

        public Task<MemoryImage> LoadThumbnailImageAsync()
            => this.LoadThumbnailImageAsync(CancellationToken.None);

        public Task<MemoryImage> LoadThumbnailImageAsync(CancellationToken cancellationToken)
            => this.LoadThumbnailImageAsync(Networking.Http, cancellationToken);

        public Task<MemoryImage> LoadThumbnailImageAsync(HttpClient http, CancellationToken cancellationToken)
            => this.LoadThumbnailImageAsync(http, null, cancellationToken);

        public Task<MemoryImage> LoadThumbnailImageAsync(IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
            => this.LoadThumbnailImageAsync(Networking.Http, progress, cancellationToken);

        public async Task<MemoryImage> LoadThumbnailImageAsync(HttpClient http, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
        {
            IThumbnailLoader CreateLoader()
                => new SimpleThumbnailLoader(this.ThumbnailImageUrl ?? throw new InvalidOperationException($"{nameof(this.ThumbnailImageUrl)} is not set"));

            var loader = this.Loader ?? CreateLoader();

            if (loader is IProgressReportingThumbnailLoader progressLoader)
            {
                return await progressLoader.Load(http, progress, cancellationToken)
                    .ConfigureAwait(false);
            }

            return await loader.Load(http, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
