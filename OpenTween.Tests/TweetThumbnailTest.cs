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

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using OpenTween.Models;
using OpenTween.SocialProtocol.Twitter;
using OpenTween.Thumbnail;
using OpenTween.Thumbnail.Services;
using Xunit;

namespace OpenTween
{
    public class TweetThumbnailTest
    {
        private ThumbnailGenerator CreateThumbnailGenerator()
        {
            var imgAzyobuziNet = new ImgAzyobuziNet(autoupdate: false);
            var thumbGenerator = new ThumbnailGenerator(imgAzyobuziNet);
            thumbGenerator.Services.Clear();
            return thumbGenerator;
        }

        private IThumbnailService CreateThumbnailService()
        {
            var thumbnailServiceMock = new Mock<IThumbnailService>();
            thumbnailServiceMock
                .Setup(
                    x => x.GetThumbnailInfoAsync("http://example.com/abcd", It.IsAny<PostClass>(), It.IsAny<CancellationToken>())
                )
                .ReturnsAsync(new ThumbnailInfo("http://example.com/abcd", "http://img.example.com/abcd.png")
                {
                    Loader = new FakeThumbnailLoader(),
                });
            thumbnailServiceMock
                .Setup(
                    x => x.GetThumbnailInfoAsync("http://example.com/efgh", It.IsAny<PostClass>(), It.IsAny<CancellationToken>())
                )
                .ReturnsAsync(new ThumbnailInfo("http://example.com/efgh", "http://img.example.com/efgh.png")
                {
                    Loader = new FakeThumbnailLoader(),
                });
            return thumbnailServiceMock.Object;
        }

        [Fact]
        public async Task PrepareThumbnails_Test()
        {
            var thumbnailGenerator = this.CreateThumbnailGenerator();
            thumbnailGenerator.Services.Add(this.CreateThumbnailService());

            var tweetThumbnail = new TweetThumbnail();
            tweetThumbnail.Initialize(thumbnailGenerator);

            var post = new PostClass
            {
                StatusId = new TwitterStatusId("100"),
                Media = new() { new("http://example.com/abcd") },
            };

            await tweetThumbnail.PrepareThumbnails(post, CancellationToken.None);

            Assert.True(tweetThumbnail.ThumbnailAvailable);
            Assert.Single(tweetThumbnail.Thumbnails);
            Assert.Equal(0, tweetThumbnail.SelectedIndex);
            Assert.Equal("http://example.com/abcd", tweetThumbnail.CurrentThumbnail.MediaPageUrl);
            Assert.Equal("http://img.example.com/abcd.png", tweetThumbnail.CurrentThumbnail.ThumbnailImageUrl);
        }

        [Fact]
        public async Task PrepareThumbnails_NoThumbnailTest()
        {
            var thumbnailGenerator = this.CreateThumbnailGenerator();
            thumbnailGenerator.Services.Add(this.CreateThumbnailService());

            var tweetThumbnail = new TweetThumbnail();
            tweetThumbnail.Initialize(thumbnailGenerator);

            var post = new PostClass
            {
                StatusId = new TwitterStatusId("100"),
                Media = new() { new("http://hoge.example.com/") },
            };

            await tweetThumbnail.PrepareThumbnails(post, CancellationToken.None);

            Assert.False(tweetThumbnail.ThumbnailAvailable);
            Assert.Throws<InvalidOperationException>(() => tweetThumbnail.Thumbnails);
        }

        [Fact]
        public async Task PrepareThumbnails_CancelTest()
        {
            var thumbnailServiceMock = new Mock<IThumbnailService>();
            thumbnailServiceMock
                .Setup(
                    x => x.GetThumbnailInfoAsync("http://slow.example.com/abcd", It.IsAny<PostClass>(), It.IsAny<CancellationToken>())
                )
                .Returns(async () =>
                {
                    await Task.Delay(200);
                    return new ThumbnailInfo("http://slow.example.com/abcd", "http://slow.example.com/abcd")
                    {
                        Loader = new FakeThumbnailLoader(),
                    };
                });

            var thumbnailGenerator = this.CreateThumbnailGenerator();
            thumbnailGenerator.Services.Add(thumbnailServiceMock.Object);

            var tweetThumbnail = new TweetThumbnail();
            tweetThumbnail.Initialize(thumbnailGenerator);

            var post = new PostClass
            {
                StatusId = new TwitterStatusId("100"),
                Media = new() { new("http://slow.example.com/abcd") },
            };

            using var tokenSource = new CancellationTokenSource();
            var task = tweetThumbnail.PrepareThumbnails(post, tokenSource.Token);
            tokenSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
            Assert.True(task.IsCanceled);
        }

        [Fact]
        public async Task PrepareThumbnails_RetryAfterCancelTest()
        {
            var thumbnailServiceMock = new Mock<IThumbnailService>();
            thumbnailServiceMock
                .Setup(
                    x => x.GetThumbnailInfoAsync("http://slow.example.com/abcd", It.IsAny<PostClass>(), It.IsAny<CancellationToken>())
                )
                .Returns(async () =>
                {
                    await Task.Delay(200);
                    return new ThumbnailInfo("http://slow.example.com/abcd", "http://slow.example.com/abcd")
                    {
                        Loader = new FakeThumbnailLoader(),
                    };
                });

            var thumbnailGenerator = this.CreateThumbnailGenerator();
            thumbnailGenerator.Services.Add(thumbnailServiceMock.Object);

            var tweetThumbnail = new TweetThumbnail();
            tweetThumbnail.Initialize(thumbnailGenerator);

            var post = new PostClass
            {
                StatusId = new TwitterStatusId("100"),
                Media = new() { new("http://slow.example.com/abcd") },
            };

            using (var tokenSource = new CancellationTokenSource())
            {
                var task = tweetThumbnail.PrepareThumbnails(post, tokenSource.Token);
                tokenSource.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
            }

            // キャンセルされた後に同じ発言を再度準備した場合も読み込まれること
            await tweetThumbnail.PrepareThumbnails(post, CancellationToken.None);

            Assert.True(tweetThumbnail.ThumbnailAvailable);
            Assert.Single(tweetThumbnail.Thumbnails);
        }

        [Fact]
        public async Task PrepareThumbnails_OverlappedSamePostTest()
        {
            var callCount = 0;
            var thumbnailServiceMock = new Mock<IThumbnailService>();
            thumbnailServiceMock
                .Setup(
                    x => x.GetThumbnailInfoAsync("http://slow.example.com/abcd", It.IsAny<PostClass>(), It.IsAny<CancellationToken>())
                )
                .Returns(async () =>
                {
                    // 1回目の呼び出しの方が先に完了する
                    var delay = Interlocked.Increment(ref callCount) == 1 ? 50 : 300;
                    await Task.Delay(delay);
                    return new ThumbnailInfo("http://slow.example.com/abcd", "http://slow.example.com/abcd")
                    {
                        Loader = new SlowThumbnailLoader(),
                    };
                });

            var thumbnailGenerator = this.CreateThumbnailGenerator();
            thumbnailGenerator.Services.Add(thumbnailServiceMock.Object);

            var tweetThumbnail = new TweetThumbnail();
            tweetThumbnail.Initialize(thumbnailGenerator);

            var loadTasks = new System.Collections.Generic.List<Task<MemoryImage>>();
            tweetThumbnail.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TweetThumbnail.ThumbnailAvailable) && tweetThumbnail.ThumbnailAvailable)
                    loadTasks.Add(tweetThumbnail.LoadSelectedThumbnail());
            };

            var post = new PostClass
            {
                StatusId = new TwitterStatusId("100"),
                Media = new() { new("http://slow.example.com/abcd") },
            };

            // 同じ発言に対する準備が重複して実行された場合
            var task1 = tweetThumbnail.PrepareThumbnails(post, CancellationToken.None);
            var task2 = tweetThumbnail.PrepareThumbnails(post, CancellationToken.None);
            await Task.WhenAll(task1, task2);

            Assert.True(tweetThumbnail.ThumbnailAvailable);

            // 表示開始時に読み込みを開始した画像が、古い方の準備の完了によってキャンセルされないこと
            var loadTask = Assert.Single(loadTasks);
            using var image = await loadTask;
            Assert.NotNull(image);
        }

        [Fact]
        public async Task LoadSelectedThumbnail_Test()
        {
            using var image = TestUtils.CreateDummyImage();
            var thumbnailLoaderMock = new Mock<IThumbnailLoader>();
            thumbnailLoaderMock
                .Setup(
                    x => x.Load(It.IsAny<HttpClient>(), It.IsAny<CancellationToken>())
                )
                .ReturnsAsync(image);

            var thumbnailServiceMock = new Mock<IThumbnailService>();
            thumbnailServiceMock
                .Setup(
                    x => x.GetThumbnailInfoAsync("http://example.com/abcd", It.IsAny<PostClass>(), It.IsAny<CancellationToken>())
                )
                .ReturnsAsync(new ThumbnailInfo("http://example.com/abcd", "http://example.com/abcd")
                {
                    Loader = thumbnailLoaderMock.Object,
                });

            var thumbnailGenerator = this.CreateThumbnailGenerator();
            thumbnailGenerator.Services.Add(thumbnailServiceMock.Object);

            var tweetThumbnail = new TweetThumbnail();
            tweetThumbnail.Initialize(thumbnailGenerator);

            var post = new PostClass
            {
                StatusId = new TwitterStatusId("100"),
                Media = new() { new("http://example.com/abcd") },
            };

            await tweetThumbnail.PrepareThumbnails(post, CancellationToken.None);

            var loadedImage = await tweetThumbnail.LoadSelectedThumbnail();
            Assert.Same(image, loadedImage);
        }

        [Fact]
        public async Task LoadSelectedThumbnail_RequestCollapsingTest()
        {
            var tsc = new TaskCompletionSource<MemoryImage>();
            var thumbnailLoaderMock = new Mock<IThumbnailLoader>();
            thumbnailLoaderMock
                .Setup(
                    x => x.Load(It.IsAny<HttpClient>(), It.IsAny<CancellationToken>())
                )
                .Returns(tsc.Task);

            var thumbnailServiceMock = new Mock<IThumbnailService>();
            thumbnailServiceMock
                .Setup(
                    x => x.GetThumbnailInfoAsync("http://example.com/abcd", It.IsAny<PostClass>(), It.IsAny<CancellationToken>())
                )
                .ReturnsAsync(new ThumbnailInfo("http://example.com/abcd", "http://example.com/abcd")
                {
                    Loader = thumbnailLoaderMock.Object,
                });

            var thumbnailGenerator = this.CreateThumbnailGenerator();
            thumbnailGenerator.Services.Add(thumbnailServiceMock.Object);

            var tweetThumbnail = new TweetThumbnail();
            tweetThumbnail.Initialize(thumbnailGenerator);

            var post = new PostClass
            {
                StatusId = new TwitterStatusId("100"),
                Media = new() { new("http://example.com/abcd") },
            };

            await tweetThumbnail.PrepareThumbnails(post, CancellationToken.None);

            var loadTask1 = tweetThumbnail.LoadSelectedThumbnail();
            await Task.Delay(50);

            // 画像のロードが完了しない間に再度 LoadSelectedThumbnail が呼ばれた場合は同一の Task を返す
            // （複数回呼ばれても画像のリクエストは一本にまとめられる）
            var loadTask2 = tweetThumbnail.LoadSelectedThumbnail();
            Assert.Same(loadTask1, loadTask2);

            using var image = TestUtils.CreateDummyImage();
            tsc.SetResult(image);

            Assert.Same(image, await loadTask1);
        }

        [Fact]
        public async Task SelectedIndex_Test()
        {
            var thumbnailGenerator = this.CreateThumbnailGenerator();
            thumbnailGenerator.Services.Add(this.CreateThumbnailService());

            var tweetThumbnail = new TweetThumbnail();
            tweetThumbnail.Initialize(thumbnailGenerator);

            var post = new PostClass
            {
                StatusId = new TwitterStatusId("100"),
                Media = new() { new("http://example.com/abcd"), new("http://example.com/efgh") },
            };

            await tweetThumbnail.PrepareThumbnails(post, CancellationToken.None);

            Assert.Equal(2, tweetThumbnail.Thumbnails.Length);
            Assert.Equal(0, tweetThumbnail.SelectedIndex);
            Assert.Equal("http://example.com/abcd", tweetThumbnail.CurrentThumbnail.MediaPageUrl);

            tweetThumbnail.SelectedIndex = 1;
            Assert.Equal(1, tweetThumbnail.SelectedIndex);
            Assert.Equal("http://example.com/efgh", tweetThumbnail.CurrentThumbnail.MediaPageUrl);

            Assert.Throws<ArgumentOutOfRangeException>(() => tweetThumbnail.SelectedIndex = -1);
            Assert.Throws<ArgumentOutOfRangeException>(() => tweetThumbnail.SelectedIndex = 2);
        }

        [Fact]
        public void SelectedIndex_NoThumbnailTest()
        {
            var thumbnailGenerator = this.CreateThumbnailGenerator();
            var tweetThumbnail = new TweetThumbnail();
            tweetThumbnail.Initialize(thumbnailGenerator);

            Assert.False(tweetThumbnail.ThumbnailAvailable);

            // サムネイルが無い場合に 0 以外の値をセットすると例外を発生させる
            tweetThumbnail.SelectedIndex = 0;
            Assert.Throws<ArgumentOutOfRangeException>(() => tweetThumbnail.SelectedIndex = -1);
            Assert.Throws<ArgumentOutOfRangeException>(() => tweetThumbnail.SelectedIndex = 1);
        }

        [Fact]
        public async Task GetImageSearchUriGoogle_Test()
        {
            var thumbnailGenerator = this.CreateThumbnailGenerator();
            thumbnailGenerator.Services.Add(this.CreateThumbnailService());

            var tweetThumbnail = new TweetThumbnail();
            tweetThumbnail.Initialize(thumbnailGenerator);

            var post = new PostClass
            {
                StatusId = new TwitterStatusId("100"),
                Media = new() { new("http://example.com/abcd") },
            };

            await tweetThumbnail.PrepareThumbnails(post, CancellationToken.None);

            Assert.Equal("http://img.example.com/abcd.png", tweetThumbnail.CurrentThumbnail.ThumbnailImageUrl);
            Assert.Equal(
                new(@"https://lens.google.com/uploadbyurl?url=http%3A%2F%2Fimg.example.com%2Fabcd.png"),
                tweetThumbnail.GetImageSearchUriGoogle()
            );
        }

        [Fact]
        public async Task GetImageSearchUriSauceNao_Test()
        {
            var thumbnailGenerator = this.CreateThumbnailGenerator();
            thumbnailGenerator.Services.Add(this.CreateThumbnailService());

            var tweetThumbnail = new TweetThumbnail();
            tweetThumbnail.Initialize(thumbnailGenerator);

            var post = new PostClass
            {
                StatusId = new TwitterStatusId("100"),
                Media = new() { new("http://example.com/abcd") },
            };

            await tweetThumbnail.PrepareThumbnails(post, CancellationToken.None);

            Assert.Equal("http://img.example.com/abcd.png", tweetThumbnail.CurrentThumbnail.ThumbnailImageUrl);
            Assert.Equal(
                new(@"https://saucenao.com/search.php?url=http%3A%2F%2Fimg.example.com%2Fabcd.png"),
                tweetThumbnail.GetImageSearchUriSauceNao()
            );
        }

        [Fact]
        public async Task Scroll_Test()
        {
            var thumbnailGenerator = this.CreateThumbnailGenerator();
            thumbnailGenerator.Services.Add(this.CreateThumbnailService());

            var tweetThumbnail = new TweetThumbnail();
            tweetThumbnail.Initialize(thumbnailGenerator);

            var post = new PostClass
            {
                StatusId = new TwitterStatusId("100"),
                Media = new() { new("http://example.com/abcd"), new("http://example.com/efgh") },
            };

            await tweetThumbnail.PrepareThumbnails(post, CancellationToken.None);

            Assert.Equal(2, tweetThumbnail.Thumbnails.Length);
            Assert.Equal(0, tweetThumbnail.SelectedIndex);

            tweetThumbnail.ScrollDown();
            Assert.Equal(1, tweetThumbnail.SelectedIndex);

            tweetThumbnail.ScrollDown();
            Assert.Equal(1, tweetThumbnail.SelectedIndex);

            tweetThumbnail.ScrollUp();
            Assert.Equal(0, tweetThumbnail.SelectedIndex);

            tweetThumbnail.ScrollUp();
            Assert.Equal(0, tweetThumbnail.SelectedIndex);
        }

        [Fact]
        public async Task CurrentLoadProgress_Test()
        {
            var loader = new ProgressThumbnailLoader();

            var thumbnailServiceMock = new Mock<IThumbnailService>();
            thumbnailServiceMock
                .Setup(
                    x => x.GetThumbnailInfoAsync("http://example.com/abcd", It.IsAny<PostClass>(), It.IsAny<CancellationToken>())
                )
                .ReturnsAsync(new ThumbnailInfo("http://example.com/abcd", "http://example.com/abcd")
                {
                    Loader = loader,
                });

            var thumbnailGenerator = this.CreateThumbnailGenerator();
            thumbnailGenerator.Services.Add(thumbnailServiceMock.Object);

            var tweetThumbnail = new TweetThumbnail();
            tweetThumbnail.Initialize(thumbnailGenerator);

            var post = new PostClass
            {
                StatusId = new TwitterStatusId("100"),
                Media = new() { new("http://example.com/abcd") },
            };

            await tweetThumbnail.PrepareThumbnails(post, CancellationToken.None);
            Assert.Null(tweetThumbnail.CurrentLoadProgress);

            var loadTask = tweetThumbnail.LoadSelectedThumbnail();
            await loader.Reported.Task;

            Assert.Equal(new DownloadProgress(100, 400), tweetThumbnail.CurrentLoadProgress);

            loader.Completion.SetResult(TestUtils.CreateDummyImage());
            using var image = await loadTask;

            // 継続タスクで進捗状況がクリアされるのを待つ
            for (var i = 0; i < 100 && tweetThumbnail.CurrentLoadProgress != null; i++)
                await Task.Delay(10);

            Assert.Null(tweetThumbnail.CurrentLoadProgress);
        }

        private class ProgressThumbnailLoader : IProgressReportingThumbnailLoader
        {
            public TaskCompletionSource<bool> Reported { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<MemoryImage> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<MemoryImage> Load(HttpClient http, CancellationToken cancellationToken)
                => this.Load(http, null, cancellationToken);

            public async Task<MemoryImage> Load(HttpClient http, IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
            {
                progress?.Report(new(100, 400));
                this.Reported.SetResult(true);
                return await this.Completion.Task;
            }
        }

        private class FakeThumbnailLoader : IThumbnailLoader
        {
            public Task<MemoryImage> Load(HttpClient http, CancellationToken cancellationToken)
                => Task.FromResult(TestUtils.CreateDummyImage());
        }

        private class SlowThumbnailLoader : IThumbnailLoader
        {
            public async Task<MemoryImage> Load(HttpClient http, CancellationToken cancellationToken)
            {
                await Task.Delay(500, cancellationToken);
                return TestUtils.CreateDummyImage();
            }
        }
    }
}
