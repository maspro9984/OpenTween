// OpenTween - Client of Twitter
// This file is licensed under the GNU General Public License version 3 or later.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using OpenTween.SocialProtocol.Twitter;
using Xunit;
using Xunit.Abstractions;

namespace OpenTween.Models
{
    public class TabModelUnreadTest
    {
        private readonly ITestOutputHelper output;

        public TabModelUnreadTest(ITestOutputHelper output)
            => this.output = output;

        [Fact]
        public void UnreadCount_DoesNotScanPostsTest()
        {
            var tab = new CountingSearchTabModel();
            tab.SetSortMode(ComparerMode.Id, SortOrder.Ascending);
            for (var i = 1; i <= 10000; i++)
            {
                tab.AddPostQueue(new PostClass
                {
                    StatusId = new TwitterStatusId(i.ToString()),
                    IsPromoted = i % 2 == 0,
                    IsRead = false,
                });
            }
            tab.AddSubmit();
            tab.SetHideFilter(x => x.IsPromoted);
            tab.PostsAccessCount = 0;

            // 選択時のタイトル・ステータス・各タブの未読表示は繰り返し件数を参照する。
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < 20; i++)
                Assert.Equal(5000, tab.UnreadCount);
            watch.Stop();
            this.output.WriteLine($"20 unread-count reads: {watch.Elapsed.TotalMilliseconds:F3} ms; post lookups: {tab.PostsAccessCount}");

            Assert.Equal(0, tab.PostsAccessCount);
        }

        [Theory]
        [InlineData(SortOrder.Ascending)]
        [InlineData(SortOrder.Descending)]
        public void UnreadCount_FilterAndReadStateChangesTest(SortOrder order)
        {
            var tab = new PublicSearchTabModel("search");
            tab.SetSortMode(ComparerMode.Id, order);
            tab.SetHideFilter(x => x.IsPromoted);

            var visible = new PostClass { StatusId = new TwitterStatusId("100"), IsRead = false };
            var hidden = new PostClass { StatusId = new TwitterStatusId("200"), IsRead = false, IsPromoted = true };
            var read = new PostClass { StatusId = new TwitterStatusId("300"), IsRead = true };
            foreach (var post in new[] { visible, hidden, read, visible })
                tab.AddPostQueue(post);
            tab.AddSubmit();
            Assert.Equal(1, tab.UnreadCount);

            Assert.True(tab.SetReadState(visible.StatusId, true));
            Assert.False(tab.SetReadState(visible.StatusId, true));
            Assert.Equal(0, tab.UnreadCount);
            Assert.True(tab.SetReadState(hidden.StatusId, true));
            Assert.True(tab.SetReadState(hidden.StatusId, false));
            Assert.Equal(0, tab.UnreadCount);
            Assert.True(tab.SetReadState(read.StatusId, false));
            Assert.False(tab.SetReadState(read.StatusId, false));
            Assert.Equal(1, tab.UnreadCount);

            tab.UnreadManage = false;
            Assert.Equal(0, tab.UnreadCount);
            tab.UnreadManage = true;
            Assert.Equal(1, tab.UnreadCount);

            tab.SetHideFilter(null);
            Assert.Equal(2, tab.UnreadCount);
            tab.SetHideFilter(x => !x.IsPromoted);
            Assert.Equal(1, tab.UnreadCount);
            tab.SetSortMode(ComparerMode.Name, order);
            Assert.Equal(1, tab.UnreadCount);

            Assert.True(tab.RemovePostImmediately(read.StatusId));
            Assert.Equal(1, tab.UnreadCount);
            Assert.True(tab.RemovePostImmediately(hidden.StatusId));
            Assert.False(tab.RemovePostImmediately(hidden.StatusId));
            Assert.Equal(0, tab.UnreadCount);

            tab.AddPostQueue(hidden);
            tab.AddSubmit();
            Assert.Equal(1, tab.UnreadCount);
            tab.ClearIDs();
            Assert.Equal(0, tab.UnreadCount);
            tab.AddPostQueue(hidden);
            tab.AddSubmit();
            Assert.Equal(1, tab.UnreadCount);
        }

        [Fact]
        public void UnreadCount_MatchesVisibleUnreadPostsTest()
        {
            var random = new Random(12345);
            var tab = new PublicSearchTabModel("search");
            var posts = Enumerable.Range(1, 100).Select(i => new PostClass
            {
                StatusId = new TwitterStatusId(i.ToString()),
                IsPromoted = i % 2 == 0,
                IsRead = i % 3 == 0,
            }).ToArray();
            bool? hidePromoted = null;

            for (var i = 0; i < 1000; i++)
            {
                var post = posts[random.Next(posts.Length)];
                switch (random.Next(6))
                {
                    case 0:
                        tab.AddPostQueue(post);
                        tab.AddSubmit();
                        break;
                    case 1:
                        tab.RemovePostImmediately(post.StatusId);
                        break;
                    case 2:
                        if (tab.Contains(post.StatusId))
                            tab.SetReadState(post.StatusId, random.Next(2) == 0);
                        break;
                    case 3:
                        hidePromoted = random.Next(3) switch { 0 => null, 1 => true, _ => false };
                        tab.SetHideFilter(hidePromoted == null ? null : x => x.IsPromoted == hidePromoted);
                        break;
                    case 4:
                        tab.SetSortMode(ComparerMode.Id, random.Next(2) == 0 ? SortOrder.Ascending : SortOrder.Descending);
                        break;
                    case 5:
                        tab.ClearIDs();
                        break;
                }

                var expected = tab.GetUnreadIds().Count(id => hidePromoted == null || tab.Posts[id].IsPromoted != hidePromoted);
                Assert.Equal(expected, tab.UnreadCount);
            }
        }

        private sealed class CountingSearchTabModel : PublicSearchTabModel
        {
            public int PostsAccessCount { get; set; }

            public override ConcurrentDictionary<PostId, PostClass> Posts
            {
                get
                {
                    this.PostsAccessCount++;
                    return base.Posts;
                }
            }

            public CountingSearchTabModel()
                : base("search")
            {
            }
        }
    }
}
