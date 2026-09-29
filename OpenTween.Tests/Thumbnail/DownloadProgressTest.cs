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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OpenTween.Thumbnail
{
    public class DownloadProgressTest
    {
        [Fact]
        public void Ratio_Test()
        {
            Assert.Equal(0.25, new DownloadProgress(250, 1000).Ratio);
            Assert.Equal(1.0, new DownloadProgress(2000, 1000).Ratio);
            Assert.Null(new DownloadProgress(250, null).Ratio);
            Assert.Null(new DownloadProgress(0, 0).Ratio);
        }

        [Fact]
        public void ToDisplayString_Test()
        {
            Assert.Equal("読み込み中...", new DownloadProgress(0, null).ToDisplayString());
            Assert.Equal("512 B", new DownloadProgress(512, null).ToDisplayString());
            Assert.Equal("100.0 KB / 1.00 MB (9%)", new DownloadProgress(100 * 1024, 1024 * 1024).ToDisplayString());
            Assert.Equal("1.00 MB / 1.00 MB (100%)", new DownloadProgress(1024 * 1024, 1024 * 1024).ToDisplayString());
        }

        [Fact]
        public async Task CopyWithProgressAsync_Test()
        {
            var data = Enumerable.Range(0, 200_000).Select(x => (byte)x).ToArray();
            using var source = new MemoryStream(data);
            using var destination = new MemoryStream();

            var reports = new List<DownloadProgress>();
            var progress = new SyncProgress(reports.Add);

            await DownloadProgress.CopyWithProgressAsync(source, destination, data.Length, progress, CancellationToken.None, reportInterval: TimeSpan.Zero);

            Assert.Equal(data, destination.ToArray());
            Assert.Equal(new DownloadProgress(0, data.Length), reports.First());
            Assert.Equal(new DownloadProgress(data.Length, data.Length), reports.Last());
            Assert.True(reports.Count > 2);

            // 受信済みのバイト数は単調増加する
            Assert.Equal(reports.Select(x => x.ReceivedBytes).OrderBy(x => x), reports.Select(x => x.ReceivedBytes));
        }

        [Fact]
        public async Task CopyWithProgressAsync_UnknownLengthTest()
        {
            var data = new byte[1000];
            using var source = new MemoryStream(data);
            using var destination = new MemoryStream();

            var reports = new List<DownloadProgress>();
            await DownloadProgress.CopyWithProgressAsync(source, destination, null, new SyncProgress(reports.Add), CancellationToken.None);

            Assert.Equal(new DownloadProgress(0, null), reports.First());

            // 完了時は受信したバイト数を全体のサイズとして通知する
            Assert.Equal(new DownloadProgress(1000, 1000), reports.Last());
        }

        private sealed class SyncProgress : IProgress<DownloadProgress>
        {
            private readonly Action<DownloadProgress> handler;

            public SyncProgress(Action<DownloadProgress> handler)
                => this.handler = handler;

            public void Report(DownloadProgress value)
                => this.handler(value);
        }
    }
}
