// OpenTween - Client of Twitter
// Copyright (c) 2013 kim_upsilon (@kim_upsilon) <https://upsilo.net/~upsilon/>
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
using System.Text.RegularExpressions;
using OpenTween.Api;
using OpenTween.Setting;
using Xunit;

namespace OpenTween.SocialProtocol.Twitter
{
    public class TwitterLegacyTest
    {
        [Theory]
        [InlineData("https://twitter.com/twitterapi/status/22634515958",
            new[] { "22634515958" })]
        [InlineData("""<a target="_self" href="https://t.co/aaaaaaaa" title="https://twitter.com/twitterapi/status/22634515958">twitter.com/twitterapi/stat…</a>""",
            new[] { "22634515958" })]
        [InlineData("""<a target="_self" href="https://t.co/bU3oR95KIy" title="https://twitter.com/haru067/status/224782458816692224">https://t.co/bU3oR95KIy</a>""" +
            """<a target="_self" href="https://t.co/bbbbbbbb" title="https://twitter.com/karno/status/311081657790771200">https://t.co/bbbbbbbb</a>""",
            new[] { "224782458816692224", "311081657790771200" })]
        [InlineData("https://mobile.twitter.com/muji_net/status/21984934471",
            new[] { "21984934471" })]
        [InlineData("https://twitter.com/imgazyobuzi/status/293333871171354624/photo/1",
            new[] { "293333871171354624" })]
        [InlineData("https://x.com/twitterapi/status/22634515958",
            new[] { "22634515958" })]
        public void StatusUrlRegexTest(string url, string[] expected)
        {
            var results = TwitterLegacy.StatusUrlRegex.Matches(url).Cast<Match>()
                .Select(x => x.Groups["StatusId"].Value).ToArray();

            Assert.Equal(expected, results);
        }

        [Theory]
        [InlineData("https://twitter.com/twitterapi/status/22634515958", true)]
        [InlineData("http://twitter.com/twitterapi/status/22634515958", true)]
        [InlineData("https://mobile.twitter.com/twitterapi/status/22634515958", true)]
        [InlineData("http://mobile.twitter.com/twitterapi/status/22634515958", true)]
        [InlineData("https://twitter.com/i/web/status/22634515958", false)]
        [InlineData("https://twitter.com/imgazyobuzi/status/293333871171354624/photo/1", false)]
        [InlineData("https://pic.twitter.com/gbxdb2Oj", false)]
        [InlineData("https://twitter.com/messages/compose?recipient_id=514241801", true)]
        [InlineData("http://twitter.com/messages/compose?recipient_id=514241801", true)]
        [InlineData("https://twitter.com/messages/compose?recipient_id=514241801&text=%E3%81%BB%E3%81%92", true)]
        [InlineData("https://x.com/twitterapi/status/22634515958", true)]
        [InlineData("https://mobile.x.com/twitterapi/status/22634515958", true)]
        [InlineData("https://x.com/messages/compose?recipient_id=514241801", false)] // DM は twitter.com のみ通る
        public void AttachmentUrlRegexTest(string url, bool isMatch)
            => Assert.Equal(isMatch, TwitterLegacy.AttachmentUrlRegex.IsMatch(url));

        [Theory]
        [InlineData("http://favstar.fm/users/twitterapi/status/22634515958", new[] { "22634515958" })]
        [InlineData("http://ja.favstar.fm/users/twitterapi/status/22634515958", new[] { "22634515958" })]
        [InlineData("http://favstar.fm/t/22634515958", new[] { "22634515958" })]
        [InlineData("http://aclog.koba789.com/i/312485321239564288", new[] { "312485321239564288" })]
        [InlineData("http://frtrt.net/solo_status.php?status=263483634307198977", new[] { "263483634307198977" })]
        public void ThirdPartyStatusUrlRegexTest(string url, string[] expected)
        {
            var results = TwitterLegacy.ThirdPartyStatusUrlRegex.Matches(url).Cast<Match>()
                .Select(x => x.Groups["StatusId"].Value).ToArray();

            Assert.Equal(expected, results);
        }

        [Fact]
        public void GetApiResultCount_DefaultTest()
        {
            var oldInstance = SettingManagerTest.Common;
            SettingManagerTest.Common = new SettingCommon();

            var timeline = SettingManager.Instance.Common.CountApi;
            var reply = SettingManager.Instance.Common.CountApiReply;
            var more = SettingManager.Instance.Common.MoreCountApi;
            var startup = SettingManager.Instance.Common.FirstCountApi;
            var favorite = SettingManager.Instance.Common.FavoritesCountApi;
            var list = SettingManager.Instance.Common.ListCountApi;
            var search = SettingManager.Instance.Common.SearchCountApi;
            var usertl = SettingManager.Instance.Common.UserTimelineCountApi;

            // デフォルト値チェック
            Assert.False(SettingManager.Instance.Common.UseAdditionalCount);
            Assert.Equal(60, timeline);
            Assert.Equal(40, reply);
            Assert.Equal(200, more);
            Assert.Equal(100, startup);
            Assert.Equal(40, favorite);
            Assert.Equal(100, list);
            Assert.Equal(100, search);
            Assert.Equal(20, usertl);

            // Timeline,Reply
            Assert.Equal(timeline, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.Timeline, false, false));
            Assert.Equal(reply, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.Reply, false, false));

            // その他はTimelineと同値になる
            Assert.Equal(timeline, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.Favorites, false, false));
            Assert.Equal(timeline, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.List, false, false));
            Assert.Equal(timeline, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.PublicSearch, false, false));
            Assert.Equal(timeline, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.UserTimeline, false, false));

            SettingManagerTest.Common = oldInstance;
        }

        [Fact]
        public void GetApiResultCount_AdditionalCountTest()
        {
            var oldInstance = SettingManagerTest.Common;
            SettingManagerTest.Common = new SettingCommon();

            var timeline = SettingManager.Instance.Common.CountApi;
            var reply = SettingManager.Instance.Common.CountApiReply;
            var more = SettingManager.Instance.Common.MoreCountApi;
            var startup = SettingManager.Instance.Common.FirstCountApi;
            var favorite = SettingManager.Instance.Common.FavoritesCountApi;
            var list = SettingManager.Instance.Common.ListCountApi;
            var search = SettingManager.Instance.Common.SearchCountApi;
            var usertl = SettingManager.Instance.Common.UserTimelineCountApi;

            SettingManager.Instance.Common.UseAdditionalCount = true;

            // Timeline
            Assert.Equal(timeline, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.Timeline, false, false));
            Assert.Equal(100, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.Timeline, true, false)); // 100 件が上限
            Assert.Equal(startup, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.Timeline, false, true));

            // Reply
            Assert.Equal(reply, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.Reply, false, false));
            Assert.Equal(more, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.Reply, true, false));
            Assert.Equal(reply, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.Reply, false, true));  // Replyの値が使われる

            // Favorites
            Assert.Equal(favorite, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.Favorites, false, false));
            Assert.Equal(favorite, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.Favorites, true, false));
            Assert.Equal(favorite, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.Favorites, false, true));

            SettingManager.Instance.Common.FavoritesCountApi = 0;

            Assert.Equal(timeline, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.Favorites, false, false));
            Assert.Equal(more, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.Favorites, true, false));
            Assert.Equal(startup, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.Favorites, false, true));

            // List
            Assert.Equal(list, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.List, false, false));
            Assert.Equal(list, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.List, true, false));
            Assert.Equal(list, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.List, false, true));

            SettingManager.Instance.Common.ListCountApi = 0;

            Assert.Equal(timeline, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.List, false, false));
            Assert.Equal(more, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.List, true, false));
            Assert.Equal(startup, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.List, false, true));

            // PublicSearch
            Assert.Equal(search, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.PublicSearch, false, false));
            Assert.Equal(search, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.PublicSearch, true, false));
            Assert.Equal(search, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.PublicSearch, false, true));

            SettingManager.Instance.Common.SearchCountApi = 0;

            Assert.Equal(timeline, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.PublicSearch, false, false));
            Assert.Equal(search, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.PublicSearch, true, false));  // MoreCountApiの値がPublicSearchの最大値に制限される
            Assert.Equal(startup, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.PublicSearch, false, true));

            // UserTimeline
            Assert.Equal(usertl, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.UserTimeline, false, false));
            Assert.Equal(usertl, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.UserTimeline, true, false));
            Assert.Equal(usertl, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.UserTimeline, false, true));

            SettingManager.Instance.Common.UserTimelineCountApi = 0;

            Assert.Equal(timeline, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.UserTimeline, false, false));
            Assert.Equal(more, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.UserTimeline, true, false));
            Assert.Equal(startup, TwitterLegacy.GetApiResultCount(MyCommon.WORKERTYPE.UserTimeline, false, true));

            SettingManagerTest.Common = oldInstance;
        }

        [Fact]
        public void GetTextLengthRemain_Test()
        {
            using var twitterApi = new TwitterApi();
            using var twitter = new TwitterLegacy(twitterApi);

            Assert.Equal(280, twitter.GetTextLengthRemain(""));
            Assert.Equal(272, twitter.GetTextLengthRemain("hogehoge"));
        }

        [Fact]
        public void GetTextLengthRemain_BlueVerifiedTest()
        {
            using var twitterApi = new TwitterApi();
            using var twitter = new TwitterLegacy(twitterApi);

            // 有料プランのアカウントでは長文ツイートの上限 (25,000 文字) が適用される
            twitter.AccountState.UpdateFromUser(new() { IdStr = "514241801", ScreenName = "OpenTween", IsBlueVerified = true });

            Assert.Equal(25_000, twitter.GetTextLengthRemain(""));
            Assert.Equal(24_992, twitter.GetTextLengthRemain("hogehoge"));
        }

        [Theory]
        [InlineData(280, false)]
        [InlineData(281, true)]
        public void IsLongTweet_Test(int length, bool expected)
        {
            using var twitterApi = new TwitterApi();
            using var twitter = new TwitterLegacy(twitterApi);

            Assert.Equal(expected, twitter.IsLongTweet(new string('a', length)));
        }

        [Fact]
        public void IsLongTweet_WeightedTest()
        {
            using var twitterApi = new TwitterApi();
            using var twitter = new TwitterLegacy(twitterApi);

            // 日本語は 1 文字で 2 文字分として数えられる
            Assert.False(twitter.IsLongTweet(new string('あ', 140)));
            Assert.True(twitter.IsLongTweet(new string('あ', 141)));
        }

        [Fact]
        public void GetTextLengthRemain_DirectMessageTest()
        {
            using var twitterApi = new TwitterApi();
            using var twitter = new TwitterLegacy(twitterApi);

            // 2015年8月から DM の文字数上限が 10,000 文字に変更された
            // https://twittercommunity.com/t/41348
            twitter.Configuration.DmTextCharacterLimit = 10000;

            Assert.Equal(10000, twitter.GetTextLengthRemain("D twitter "));
            Assert.Equal(9992, twitter.GetTextLengthRemain("D twitter hogehoge"));

            // t.co に短縮される分の文字数を考慮
            twitter.Configuration.ShortUrlLength = 20;
            Assert.Equal(9971, twitter.GetTextLengthRemain("D twitter hogehoge http://example.com/"));

            twitter.Configuration.ShortUrlLengthHttps = 21;
            Assert.Equal(9970, twitter.GetTextLengthRemain("D twitter hogehoge https://example.com/"));
        }

        [Fact]
        public void GetTextLengthRemain_UrlTest()
        {
            using var twitterApi = new TwitterApi();
            using var twitter = new TwitterLegacy(twitterApi);

            // t.co に短縮される分の文字数を考慮
            twitter.TextConfiguration.TransformedURLLength = 20;
            Assert.Equal(260, twitter.GetTextLengthRemain("http://example.com/"));
            Assert.Equal(260, twitter.GetTextLengthRemain("http://example.com/hogehoge"));
            Assert.Equal(251, twitter.GetTextLengthRemain("hogehoge http://example.com/"));

            Assert.Equal(260, twitter.GetTextLengthRemain("https://example.com/"));
            Assert.Equal(260, twitter.GetTextLengthRemain("https://example.com/hogehoge"));
            Assert.Equal(251, twitter.GetTextLengthRemain("hogehoge https://example.com/"));
        }

        [Fact]
        public void GetTextLengthRemain_UrlWithoutSchemeTest()
        {
            using var twitterApi = new TwitterApi();
            using var twitter = new TwitterLegacy(twitterApi);

            // t.co に短縮される分の文字数を考慮
            twitter.TextConfiguration.TransformedURLLength = 20;
            Assert.Equal(260, twitter.GetTextLengthRemain("example.com"));
            Assert.Equal(260, twitter.GetTextLengthRemain("example.com/hogehoge"));
            Assert.Equal(251, twitter.GetTextLengthRemain("hogehoge example.com"));

            // スキーム (http://) を省略かつ末尾が ccTLD の場合は t.co に短縮されない
            Assert.Equal(270, twitter.GetTextLengthRemain("example.jp"));
            // ただし、末尾にパスが続く場合は t.co に短縮される
            Assert.Equal(260, twitter.GetTextLengthRemain("example.jp/hogehoge"));
        }

        [Fact]
        public void GetTextLengthRemain_SurrogatePairTest()
        {
            using var twitterApi = new TwitterApi();
            using var twitter = new TwitterLegacy(twitterApi);

            Assert.Equal(278, twitter.GetTextLengthRemain("🍣"));
            Assert.Equal(267, twitter.GetTextLengthRemain("🔥🐔🔥 焼き鳥"));
        }

        [Fact]
        public void GetTextLengthRemain_EmojiTest()
        {
            using var twitterApi = new TwitterApi();
            using var twitter = new TwitterLegacy(twitterApi);

            // 絵文字の文字数カウントの仕様変更に対するテストケース
            // https://twittercommunity.com/t/114607

            Assert.Equal(279, twitter.GetTextLengthRemain("©")); // 基本多言語面の絵文字
            Assert.Equal(277, twitter.GetTextLengthRemain("©\uFE0E")); // 異字体セレクタ付き (text style)
            Assert.Equal(279, twitter.GetTextLengthRemain("©\uFE0F")); // 異字体セレクタ付き (emoji style)
            Assert.Equal(278, twitter.GetTextLengthRemain("🍣")); // 拡張面の絵文字
            Assert.Equal(279, twitter.GetTextLengthRemain("#⃣")); // 合字で表現される絵文字
            Assert.Equal(278, twitter.GetTextLengthRemain("👦\U0001F3FF")); // Emoji modifier 付きの絵文字
            Assert.Equal(278, twitter.GetTextLengthRemain("\U0001F3FF")); // Emoji modifier 単体
            Assert.Equal(278, twitter.GetTextLengthRemain("👨\u200D🎨")); // ZWJ で結合された絵文字
            Assert.Equal(278, twitter.GetTextLengthRemain("🏃\u200D♀\uFE0F")); // ZWJ と異字体セレクタを含む絵文字
        }

        [Fact]
        public void GetTextLengthRemain_BrokenSurrogateTest()
        {
            using var twitterApi = new TwitterApi();
            using var twitter = new TwitterLegacy(twitterApi);

            // 投稿欄に IME から絵文字を入力すると HighSurrogate のみ入力された状態で TextChanged イベントが呼ばれることがある
            Assert.Equal(278, twitter.GetTextLengthRemain("\ud83d"));
            Assert.Equal(9999, twitter.GetTextLengthRemain("D twitter \ud83d"));
        }
    }
}
