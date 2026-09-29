// OpenTween - Client of Twitter
// Copyright (c) 2024 kim_upsilon (@kim_upsilon) <https://upsilo.net/~upsilon/>
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

namespace OpenTween.Thumbnail
{
    public class SimpleThumbnailLoader : IProgressReportingThumbnailLoader
    {
        private readonly string imageUrl;

        public SimpleThumbnailLoader(string imageUrl)
            => this.imageUrl = imageUrl;

        public Task<MemoryImage> Load(HttpClient http, CancellationToken cancellationToken)
            => this.Load(http, null, cancellationToken);

        public async Task<MemoryImage> Load(HttpClient http, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
        {
            MemoryImage? image = null;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, this.imageUrl);
                image = await LoadWithProgressAsync(http, request, progress, cancellationToken)
                    .ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                return image;
            }
            catch (OperationCanceledException)
            {
                image?.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 指定されたリクエストで画像を取得する。本文の受信中は <paramref name="progress"/> に進捗状況を通知する
        /// </summary>
        public static async Task<MemoryImage> LoadWithProgressAsync(
            HttpClient http,
            HttpRequestMessage request,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Report(new(0, null));

            // 本文の受信状況を通知するため、ヘッダーの受信完了時点で制御を戻させる
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            // .NET Framework のレスポンスストリームは ReadAsync のキャンセルに対応していないため、
            // キャンセル時はレスポンスを破棄して読み込みを中断させる
            using var registration = cancellationToken.Register(() => response.Dispose());

            using var imageStream = await response.Content.ReadAsStreamAsync()
                .ConfigureAwait(false);

            var totalBytes = response.Content.Headers.ContentLength;

            try
            {
                return await MemoryImage.CopyFromStreamAsync(imageStream, totalBytes, progress, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (cancellationToken.IsCancellationRequested && ex is not OperationCanceledException)
            {
                // レスポンスの破棄によって発生した例外はキャンセルとして扱う
                throw new OperationCanceledException(cancellationToken);
            }
        }
    }
}
