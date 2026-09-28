// OpenTween - Client of Twitter
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
using System.Linq;
using OpenTween.SocialProtocol.Twitter;
using Xunit;

namespace OpenTween.Models
{
    public class PostHideFilterTest
    {
        private static PostClass CreatePost(string id, string screenName = "user", bool? followed = true)
            => new()
            {
                StatusId = new TwitterStatusId(id),
                ScreenName = screenName,
                IsAuthorFollowed = followed,
            };

        [Fact]
        public void Create_NoConditionTest()
        {
            var filter = PostHideFilter.Create(new TabHideSettings(), Array.Empty<HideCategory>());
            Assert.Null(filter);
        }

        [Fact]
        public void Create_HideRetweetsTest()
        {
            var filter = PostHideFilter.Create(new TabHideSettings { HideRetweets = true }, Array.Empty<HideCategory>())!;

            Assert.False(filter(CreatePost("100")));
            Assert.True(filter(CreatePost("110") with { RetweetedId = new TwitterStatusId("50"), RetweetedBy = "friend" }));
        }

        [Fact]
        public void Create_HidePromotedTest()
        {
            var filter = PostHideFilter.Create(new TabHideSettings { HidePromoted = true }, Array.Empty<HideCategory>())!;

            Assert.False(filter(CreatePost("100")));
            Assert.True(filter(CreatePost("110") with { IsPromoted = true }));
        }

        [Fact]
        public void Create_HideNonFollowingTest()
        {
            var filter = PostHideFilter.Create(new TabHideSettings { HideNonFollowing = true }, Array.Empty<HideCategory>())!;

            Assert.False(filter(CreatePost("100", followed: true)));
            Assert.True(filter(CreatePost("110", followed: false)));

            // フォロー状態が不明な発言・自分の発言は隠さない
            Assert.False(filter(CreatePost("120", followed: null)));
            Assert.False(filter(CreatePost("130", followed: false) with { IsMe = true }));

            // リツイートは RT の非表示設定に委ねる
            Assert.False(filter(CreatePost("140", followed: false) with { RetweetedId = new TwitterStatusId("50"), RetweetedBy = "friend" }));
        }

        [Fact]
        public void Create_HiddenCategoriesTest()
        {
            var categories = new[]
            {
                new HideCategory { Name = "ニュース", ScreenNames = new[] { "@NewsSite", "news2" } },
                new HideCategory { Name = "その他", ScreenNames = new[] { "other" } },
            };
            var settings = new TabHideSettings { HiddenCategories = new[] { "ニュース" } };
            var filter = PostHideFilter.Create(settings, categories)!;

            Assert.True(filter(CreatePost("100", screenName: "newssite")));
            Assert.True(filter(CreatePost("110", screenName: "news2")));
            Assert.False(filter(CreatePost("120", screenName: "other")));

            // 非表示カテゴリのユーザーによるリツイートも隠す
            Assert.True(filter(CreatePost("130", screenName: "someone") with { RetweetedId = new TwitterStatusId("50"), RetweetedBy = "NewsSite" }));
        }

        [Fact]
        public void Create_DisabledTest()
        {
            var categories = new[] { new HideCategory { Name = "ニュース", ScreenNames = new[] { "news" } } };
            var settings = new TabHideSettings
            {
                HideRetweets = true,
                HiddenCategories = new[] { "ニュース" },
                Enabled = false,
            };

            // フィルター表示が off の場合は条件・カテゴリの設定に関わらず隠さない
            Assert.Null(PostHideFilter.Create(settings, categories));
            Assert.True(settings.HasAnyCondition);
            Assert.False(settings.IsActive);

            settings.Enabled = true;
            var filter = PostHideFilter.Create(settings, categories)!;
            Assert.True(filter(CreatePost("100", screenName: "news")));
            Assert.True(settings.IsActive);
        }

        [Fact]
        public void FollowingOnly_Test()
        {
            var settings = new TabHideSettings { FollowingOnly = true };

            Assert.True(settings.HideRetweets);
            Assert.True(settings.HidePromoted);
            Assert.True(settings.HideNonFollowing);
            Assert.True(settings.FollowingOnly);

            settings.HidePromoted = false;
            Assert.False(settings.FollowingOnly);
        }

        [Fact]
        public void TabModel_SetHideFilterTest()
        {
            var tab = new PublicSearchTabModel("search")
            {
                UnreadManage = true,
            };

            tab.AddPostQueue(CreatePost("100") with { IsRead = false });
            tab.AddPostQueue(CreatePost("110") with { IsRead = false, RetweetedId = new TwitterStatusId("50"), RetweetedBy = "friend" });
            tab.AddPostQueue(CreatePost("120") with { IsRead = false });
            tab.AddSubmit();

            Assert.Equal(3, tab.AllCount);
            Assert.Equal(3, tab.UnreadCount);

            tab.SetHideFilter(PostHideFilter.Create(new TabHideSettings { HideRetweets = true }, Array.Empty<HideCategory>()));

            // 表示上は RT を除いた 2 件となるが、タブへの所属は維持される
            Assert.Equal(2, tab.AllCount);
            Assert.Equal(3, tab.TotalCount);
            Assert.Equal(2, tab.UnreadCount);
            Assert.Equal(new TwitterStatusId("120"), tab.GetStatusIdAt(1));
            Assert.Equal(-1, tab.IndexOf(new TwitterStatusId("110")));
            Assert.True(tab.Contains(new TwitterStatusId("110")));

            // 非表示中に追加された発言も判定される
            tab.AddPostQueue(CreatePost("130") with { RetweetedId = new TwitterStatusId("60"), RetweetedBy = "friend" });
            tab.AddPostQueue(CreatePost("140"));
            tab.AddSubmit();

            Assert.Equal(3, tab.AllCount);
            Assert.Equal(5, tab.TotalCount);

            // 解除すると全て表示される
            tab.SetHideFilter(null);

            Assert.Equal(5, tab.AllCount);
            Assert.Equal(1, tab.IndexOf(new TwitterStatusId("110")));
        }

        [Fact]
        public void TabModel_NextUnreadIdTest()
        {
            var tab = new PublicSearchTabModel("search")
            {
                UnreadManage = true,
            };
            tab.SetSortMode(ComparerMode.Id, System.Windows.Forms.SortOrder.Ascending);

            tab.AddPostQueue(CreatePost("100") with { IsRead = false, IsPromoted = true });
            tab.AddPostQueue(CreatePost("110") with { IsRead = false });
            tab.AddSubmit();

            Assert.Equal(new TwitterStatusId("100"), tab.NextUnreadId);

            tab.SetHideFilter(PostHideFilter.Create(new TabHideSettings { HidePromoted = true }, Array.Empty<HideCategory>()));

            // 非表示の発言は未読として扱わない
            Assert.Equal(new TwitterStatusId("110"), tab.NextUnreadId);
        }
    }
}
