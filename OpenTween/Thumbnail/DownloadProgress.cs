// OpenTween - Client of Twitter
// Copyright (c) 2026 OpenTween contributors
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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTween.Thumbnail
{
    /// <summary>画像のダウンロードの進捗状況</summary>
    /// <param name="ReceivedBytes">受信済みのバイト数</param>
    /// <param name="TotalBytes">全体のバイト数（Content-Length が不明な場合は null）</param>
    public readonly record struct DownloadProgress(long ReceivedBytes, long? TotalBytes)
    {
        /// <summary>進捗率 (0.0～1.0)。全体のサイズが不明な場合は null</summary>
        public double? Ratio
            => this.TotalBytes is > 0 and var total
                ? Math.Min(1.0, (double)this.ReceivedBytes / total)
                : null;

        public string ToDisplayString()
        {
            if (this.ReceivedBytes == 0 && this.TotalBytes == null)
                return "読み込み中...";

            var received = FormatBytes(this.ReceivedBytes);
            if (this.Ratio is { } ratio)
                return $"{received} / {FormatBytes(this.TotalBytes!.Value)} ({(int)(ratio * 100)}%)";

            return received;
        }

        public override string ToString()
            => this.ToDisplayString();

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            if (bytes < 1024 * 1024)
                return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";

            return (bytes / 1024.0 / 1024.0).ToString("0.00", CultureInfo.InvariantCulture) + " MB";
        }

        /// <summary>
        /// ストリームの内容を <paramref name="destination"/> にコピーしながら進捗状況を通知する
        /// </summary>
        /// <remarks>
        /// 通知が過剰に発生しないよう、途中経過は <paramref name="reportInterval"/> 毎に間引いて通知する。
        /// 開始時と完了時は必ず通知する
        /// </remarks>
        public static async Task CopyWithProgressAsync(
            Stream source,
            Stream destination,
            long? totalBytes,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken,
            TimeSpan? reportInterval = null)
        {
            const int BufferSize = 81920;

            var interval = reportInterval ?? TimeSpan.FromMilliseconds(50);
            var buffer = new byte[BufferSize];
            var received = 0L;
            var stopwatch = Stopwatch.StartNew();

            progress?.Report(new(0, totalBytes));

            while (true)
            {
                var count = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                    .ConfigureAwait(false);

                if (count == 0)
                    break;

                await destination.WriteAsync(buffer, 0, count, cancellationToken)
                    .ConfigureAwait(false);

                received += count;

                if (progress != null && stopwatch.Elapsed >= interval)
                {
                    progress.Report(new(received, totalBytes));
                    stopwatch.Restart();
                }
            }

            progress?.Report(new(received, totalBytes ?? received));
        }
    }
}
