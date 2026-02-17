// OpenTween - Client of Twitter
// Copyright (c) 2014 kim_upsilon (@kim_upsilon) <https://upsilo.net/~upsilon/>
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
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using OpenTween.Api.DataModel;
using OpenTween.Controls;
using OpenTween.Models;
using OpenTween.OpenTweenCustomControl;
using OpenTween.Setting;
using OpenTween.SocialProtocol;
using OpenTween.SocialProtocol.Twitter;
using OpenTween.Thumbnail;
using Xunit;

namespace OpenTween
{
    public class TweenMainTest
    {
        private record TestContext(
            SettingManager Settings,
            TabInformations TabInfo
        );

        private void UsingTweenMain(Action<TweenMain, TestContext> func)
        {
            var settings = new SettingManager("");
            var tabinfo = new TabInformations();
            using var accounts = new AccountCollection();
            using var imageCache = new ImageCache();
            using var iconAssets = new IconAssetsManager();
            var thumbnailGenerator = new ThumbnailGenerator(new(autoupdate: false));

            // TabInformation.GetInstance() で取得できるようにする
            var field = typeof(TabInformations).GetField("Instance",
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.SetField);
            field.SetValue(null, tabinfo);

            tabinfo.AddDefaultTabs();

            using var tweenMain = new TweenMain(settings, tabinfo, accounts, imageCache, iconAssets, thumbnailGenerator);
            var context = new TestContext(settings, tabinfo);

            func(tweenMain, context);
        }

        [WinFormsFact]
        public void Initialize_Test()
            => this.UsingTweenMain((_, _) => { });

        [WinFormsFact]
        public void AddNewTab_FilterTabTest()
        {
            this.UsingTweenMain((tweenMain, context) =>
            {
                Assert.Equal(4, tweenMain.ListTab.TabCount);

                var tab = new FilterTabModel("hoge");
                context.TabInfo.AddTab(tab);
                tweenMain.AddNewTab(tab, startup: false);

                Assert.Equal(5, tweenMain.ListTab.TabCount);

                var contentPanel = tweenMain.ListTab.GetContentPanelAt(4);
                Assert.Equal("hoge", contentPanel.TabName);
                Assert.IsType<DetailsListView>(contentPanel.ListView);
            });
        }

        [WinFormsFact]
        public void AddNewTab_UserTimelineTabTest()
        {
            this.UsingTweenMain((tweenMain, context) =>
            {
                Assert.Equal(4, tweenMain.ListTab.TabCount);

                var tab = new UserTimelineTabModel("hoge", "twitterapi");
                context.TabInfo.AddTab(tab);
                tweenMain.AddNewTab(tab, startup: false);

                Assert.Equal(5, tweenMain.ListTab.TabCount);

                var contentPanel = tweenMain.ListTab.GetContentPanelAt(4);
                Assert.Equal("hoge", contentPanel.TabName);
                Assert.IsType<DetailsListView>(contentPanel.ListView);

                var header = Assert.IsType<GeneralTimelineHeaderPanel>(contentPanel.HeaderPanel);
                Assert.Equal("twitterapi's Timeline", header.HeaderText);
            });
        }

        [WinFormsFact]
        public void AddNewTab_ListTimelineTabTest()
        {
            this.UsingTweenMain((tweenMain, context) =>
            {
                Assert.Equal(4, tweenMain.ListTab.TabCount);

                var list = new ListElement
                {
                    Id = 12345L,
                    Name = "tetete",
                    Username = "opentween",
                    IsPublic = false,
                };
                var tab = new ListTimelineTabModel("hoge", list);
                context.TabInfo.AddTab(tab);
                tweenMain.AddNewTab(tab, startup: false);

                Assert.Equal(5, tweenMain.ListTab.TabCount);

                var contentPanel = tweenMain.ListTab.GetContentPanelAt(4);
                Assert.Equal("hoge", contentPanel.TabName);
                Assert.IsType<DetailsListView>(contentPanel.ListView);

                var header = Assert.IsType<GeneralTimelineHeaderPanel>(contentPanel.HeaderPanel);
                Assert.Equal("@opentween/tetete [Protected]", header.HeaderText);
            });
        }

        [WinFormsFact]
        public void AddNewTab_PublicSearchTabTest()
        {
            this.UsingTweenMain((tweenMain, context) =>
            {
                Assert.Equal(4, tweenMain.ListTab.TabCount);

                var tab = new PublicSearchTabModel("hoge")
                {
                    SearchWords = "#OpenTween",
                    SearchLang = "ja",
                };
                context.TabInfo.AddTab(tab);
                tweenMain.AddNewTab(tab, startup: false);

                Assert.Equal(5, tweenMain.ListTab.TabCount);

                var contentPanel = tweenMain.ListTab.GetContentPanelAt(4);
                Assert.Equal("hoge", contentPanel.TabName);
                Assert.IsType<DetailsListView>(contentPanel.ListView);

                var header = Assert.IsType<PublicSearchHeaderPanel>(contentPanel.HeaderPanel);
                Assert.Equal("#OpenTween", header.Query);
                Assert.Equal("ja", header.Lang);
            });
        }

        [WinFormsFact]
        public void RemoveSpecifiedTab_Test()
        {
            this.UsingTweenMain((tweenMain, context) =>
            {
                Assert.Equal(4, tweenMain.ListTab.TabCount);

                var tab = new PublicSearchTabModel("hoge")
                {
                    SearchWords = "#OpenTween",
                    SearchLang = "ja",
                };
                context.TabInfo.AddTab(tab);
                tweenMain.AddNewTab(tab, startup: false);
                Assert.Equal(5, tweenMain.ListTab.TabCount);

                var contentPanel = tweenMain.ListTab.GetContentPanelAt(4);
                var listView = contentPanel.ListView;
                var header = (PublicSearchHeaderPanel)contentPanel.HeaderPanel!;
                Assert.Equal("hoge", contentPanel.TabName);

                tweenMain.RemoveSpecifiedTab("hoge", confirm: false);

                Assert.Equal(4, tweenMain.ListTab.TabCount);
                Assert.False(context.TabInfo.ContainsTab("hoge"));
                Assert.True(contentPanel.IsDisposed);
                Assert.True(listView.IsDisposed);
                Assert.True(header.IsDisposed);
            });
        }

        [WinFormsFact]
        public void RefreshTimeline_Test()
        {
            this.UsingTweenMain((tweenMain, context) =>
            {
                var contentPanel = tweenMain.ListTab.GetContentPanelAt(0);
                Assert.Equal("Recent", contentPanel.TabName);

                var listView = contentPanel.ListView;
                Assert.Equal(0, listView.VirtualListSize);

                var post = new PostClass
                {
                    StatusId = new TwitterStatusId("100"),
                    Text = "hoge",
                    UserId = new TwitterUserId("111"),
                    ScreenName = "opentween",
                    CreatedAt = new(2024, 1, 1, 0, 0, 0),
                };
                context.TabInfo.AddPost(post);
                context.TabInfo.DistributePosts();
                tweenMain.RefreshTimeline();

                Assert.Equal(1, listView.VirtualListSize);

                var listItem = listView.Items[0];
                Assert.Equal("opentween", listItem.SubItems[4].Text);
            });
        }

        [WinFormsFact]
        public void FormatStatusText_NewLineTest()
        {
            this.UsingTweenMain((tweenMain, _) =>
            {
                var param = new PostStatusParams(Text: "aaa\r\nbbb");
                var expected = new PostStatusParams(Text: "aaa\nbbb");
                Assert.Equal(expected, tweenMain.FormatStatusText(param));
            });
        }

        [WinFormsFact]
        public void FormatStatusText_NewLineInDMTest()
        {
            this.UsingTweenMain((tweenMain, _) =>
            {
                // DM にも適用する
                var param = new PostStatusParams(Text: "D opentween aaa\r\nbbb");
                var expected = new PostStatusParams(Text: "D opentween aaa\nbbb");
                Assert.Equal(expected, tweenMain.FormatStatusText(param));
            });
        }

        [WinFormsFact]
        public void FormatStatusText_SeparateUrlAndFullwidthCharacter_EnabledTest()
        {
            this.UsingTweenMain((tweenMain, context) =>
            {
                tweenMain.SeparateUrlAndFullwidthCharacter = true;

                var param = new PostStatusParams(Text: "https://example.com/あああ");
                var expected = new PostStatusParams(Text: "https://example.com/ あああ");
                Assert.Equal(expected, tweenMain.FormatStatusText(param));
            });
        }

        [WinFormsFact]
        public void FormatStatusText_SeparateUrlAndFullwidthCharacter_DisabledTest()
        {
            this.UsingTweenMain((tweenMain, context) =>
            {
                tweenMain.SeparateUrlAndFullwidthCharacter = false;

                var param = new PostStatusParams(Text: "https://example.com/あああ");
                var expected = new PostStatusParams(Text: "https://example.com/あああ");
                Assert.Equal(expected, tweenMain.FormatStatusText(param));
            });
        }

        [WinFormsFact]
        public void FormatStatusText_ReplaceFullwidthSpace_EnabledTest()
        {
            this.UsingTweenMain((tweenMain, context) =>
            {
                context.Settings.Common.WideSpaceConvert = true;

                var param = new PostStatusParams(Text: "aaa　bbb");
                var expected = new PostStatusParams(Text: "aaa bbb");
                Assert.Equal(expected, tweenMain.FormatStatusText(param));
            });
        }

        [WinFormsFact]
        public void FormatStatusText_ReplaceFullwidthSpaceInDM_EnabledTest()
        {
            this.UsingTweenMain((tweenMain, context) =>
            {
                context.Settings.Common.WideSpaceConvert = true;

                // DM にも適用する
                var param = new PostStatusParams(Text: "D opentween aaa　bbb");
                var expected = new PostStatusParams(Text: "D opentween aaa bbb");
                Assert.Equal(expected, tweenMain.FormatStatusText(param));
            });
        }

        [WinFormsFact]
        public void FormatStatusText_ReplaceFullwidthSpace_DisabledTest()
        {
            this.UsingTweenMain((tweenMain, context) =>
            {
                context.Settings.Common.WideSpaceConvert = false;

                var param = new PostStatusParams(Text: "aaa　bbb");
                var expected = new PostStatusParams(Text: "aaa　bbb");
                Assert.Equal(expected, tweenMain.FormatStatusText(param));
            });
        }

        [WinFormsFact]
        public void FormatStatusText_UseRecommendedFooter_Test()
        {
            this.UsingTweenMain((tweenMain, context) =>
            {
                context.Settings.Local.UseRecommendStatus = true;

                var param = new PostStatusParams(Text: "aaa");
                Assert.Matches(new Regex(@"^aaa \[TWNv\d+\]$"), tweenMain.FormatStatusText(param).Text);
            });
        }

        [WinFormsFact]
        public void FormatStatusText_CustomFooterText_Test()
        {
            this.UsingTweenMain((tweenMain, context) =>
            {
                context.Settings.Local.StatusText = "foo";

                var param = new PostStatusParams(Text: "aaa");
                var expected = new PostStatusParams(Text: "aaa foo");
                Assert.Equal(expected, tweenMain.FormatStatusText(param));
            });
        }

        [WinFormsFact]
        public void FormatStatusText_DisableFooterIfSendingDM_Test()
        {
            this.UsingTweenMain((tweenMain, context) =>
            {
                context.Settings.Local.StatusText = "foo";

                // DM の場合はフッターを無効化する
                var param = new PostStatusParams(Text: "D opentween aaa");
                var expected = new PostStatusParams(Text: "D opentween aaa");
                Assert.Equal(expected, tweenMain.FormatStatusText(param));
            });
        }

        [WinFormsFact]
        public void FormatStatusText_DisableFooterIfContainsUnofficialRT_Test()
        {
            this.UsingTweenMain((tweenMain, context) =>
            {
                context.Settings.Local.StatusText = "foo";

                // 非公式 RT を含む場合はフッターを無効化する
                var param = new PostStatusParams(Text: "aaa RT @foo: bbb");
                var expected = new PostStatusParams(Text: "aaa RT @foo: bbb");
                Assert.Equal(expected, tweenMain.FormatStatusText(param));
            });
        }

        [WinFormsFact]
        public void FormatStatusText_DisableFooterIfPostByEnterAndPressedShiftKey_Test()
        {
            this.UsingTweenMain((tweenMain, context) =>
            {
                context.Settings.Common.PostCtrlEnter = false;
                context.Settings.Common.PostShiftEnter = false; // Enter で投稿する設定
                context.Settings.Local.StatusText = "foo";

                // 複数行モードでは Ctrl キーでフッター無効化（Shift では無効化されない）
                var param = new PostStatusParams(Text: "aaa");
                var expectedWithFooter = new PostStatusParams(Text: "aaa foo");
                Assert.Equal(expectedWithFooter, tweenMain.FormatStatusText(param, modifierKeys: Keys.Shift));
            });
        }

        [WinFormsFact]
        public void FormatStatusText_DisableFooterIfPostByEnterAndPressedCtrlKeyAndMultilineMode_Test()
        {
            this.UsingTweenMain((tweenMain, context) =>
            {
                context.Settings.Common.PostCtrlEnter = false;
                context.Settings.Common.PostShiftEnter = false; // Enter で投稿する設定
                context.Settings.Local.StatusText = "foo";
                context.Settings.Local.StatusMultiline = true; // 複数行モード

                // Ctrl キーが押されている場合はフッターを無効化する
                var param = new PostStatusParams(Text: "aaa");
                var expected = new PostStatusParams(Text: "aaa");
                Assert.Equal(expected, tweenMain.FormatStatusText(param, modifierKeys: Keys.Control));
            });
        }

        [WinFormsFact]
        public void FormatStatusText_DisableFooterIfPostByShiftEnterAndPressedControlKey_Test()
        {
            this.UsingTweenMain((tweenMain, context) =>
            {
                context.Settings.Common.PostCtrlEnter = false;
                context.Settings.Common.PostShiftEnter = true; // Shift+Enter で投稿する設定
                context.Settings.Local.StatusText = "foo";

                // Ctrl キーが押されている場合はフッターを無効化する
                var param = new PostStatusParams(Text: "aaa");
                var expected = new PostStatusParams(Text: "aaa");
                Assert.Equal(expected, tweenMain.FormatStatusText(param, modifierKeys: Keys.Control));
            });
        }

        [WinFormsFact]
        public void FormatStatusText_EnableFooterIfPostByShiftEnter_Test()
        {
            this.UsingTweenMain((tweenMain, context) =>
            {
                context.Settings.Common.PostCtrlEnter = false;
                context.Settings.Common.PostShiftEnter = true; // Shift+Enter で投稿する設定
                context.Settings.Local.StatusText = "foo";

                // Shift+Enter で投稿する場合、Ctrl キーが押されていなければフッターを付ける
                var param = new PostStatusParams(Text: "aaa");
                var expected = new PostStatusParams(Text: "aaa foo");
                Assert.Equal(expected, tweenMain.FormatStatusText(param, modifierKeys: Keys.Shift));
            });
        }

        [WinFormsFact]
        public void FormatStatusText_DisableFooterIfPostByCtrlEnterAndPressedShiftKey_Test()
        {
            this.UsingTweenMain((tweenMain, context) =>
            {
                context.Settings.Common.PostCtrlEnter = true; // Ctrl+Enter で投稿する設定
                context.Settings.Common.PostShiftEnter = false;
                context.Settings.Local.StatusText = "foo";

                // Shift キーが押されている場合はフッターを無効化する
                var param = new PostStatusParams(Text: "aaa");
                var expected = new PostStatusParams(Text: "aaa");
                Assert.Equal(expected, tweenMain.FormatStatusText(param, modifierKeys: Keys.Shift));
            });
        }

        [WinFormsFact]
        public void FormatStatusText_EnableFooterIfPostByCtrlEnter_Test()
        {
            this.UsingTweenMain((tweenMain, context) =>
            {
                context.Settings.Common.PostCtrlEnter = true; // Ctrl+Enter で投稿する設定
                context.Settings.Common.PostShiftEnter = false;
                context.Settings.Local.StatusText = "foo";

                // Ctrl+Enter で投稿する場合、Shift キーが押されていなければフッターを付ける
                var param = new PostStatusParams(Text: "aaa");
                var expected = new PostStatusParams(Text: "aaa foo");
                Assert.Equal(expected, tweenMain.FormatStatusText(param, modifierKeys: Keys.Control));
            });
        }

        [WinFormsFact]
        public void FormatStatusText_PreventSmsCommand_Test()
        {
            this.UsingTweenMain((tweenMain, context) =>
            {
                // 「D+」などから始まる文字列をツイートしようとすると SMS コマンドと誤認されてエラーが返される問題の回避策
                var param = new PostStatusParams(Text: "d+aaaa");
                var expected = new PostStatusParams(Text: "\u200bd+aaaa");
                Assert.Equal(expected, tweenMain.FormatStatusText(param));
            });
        }

        [Fact]
        public void GetUrlFromDataObject_XMozUrlTest()
        {
            var dataBytes = Encoding.Unicode.GetBytes("https://twitter.com/\nTwitter\0");
            using var memstream = new MemoryStream(dataBytes);
            var data = new DataObject("text/x-moz-url", memstream);

            var expected = ("https://twitter.com/", "Twitter");
            Assert.Equal(expected, TweenMain.GetUrlFromDataObject(data));
        }

        [Fact]
        public void GetUrlFromDataObject_IESiteModeToUrlTest()
        {
            var dataBytes = Encoding.Unicode.GetBytes("https://twitter.com/\0Twitter\0");
            using var memstream = new MemoryStream(dataBytes);
            var data = new DataObject("IESiteModeToUrl", memstream);

            var expected = ("https://twitter.com/", "Twitter");
            Assert.Equal(expected, TweenMain.GetUrlFromDataObject(data));
        }

        [Fact]
        public void GetUrlFromDataObject_UniformResourceLocatorWTest()
        {
            var dataBytes = Encoding.Unicode.GetBytes("https://twitter.com/\0");
            using var memstream = new MemoryStream(dataBytes);
            var data = new DataObject("UniformResourceLocatorW", memstream);

            var expected = ("https://twitter.com/", (string?)null);
            Assert.Equal(expected, TweenMain.GetUrlFromDataObject(data));
        }

        [Fact]
        public void GetUrlFromDataObject_UnknownFormatTest()
        {
            using var memstream = new MemoryStream(Array.Empty<byte>());
            var data = new DataObject("application/x-hogehoge", memstream);

            Assert.Throws<NotSupportedException>(() => TweenMain.GetUrlFromDataObject(data));
        }

        [Fact]
        public void CreateRetweetUnofficial_UrlTest()
        {
            var statusText = """<a href="http://t.co/KYi7vMZzRt" title="http://twitter.com/">twitter.com</a>""";

            Assert.Equal("http://twitter.com/", TweenMain.CreateRetweetUnofficial(statusText, false));
        }

        [Fact]
        public void CreateRetweetUnofficial_MentionTest()
        {
            var statusText = """<a class="mention" href="https://twitter.com/twitterapi">@TwitterAPI</a>""";

            Assert.Equal("@TwitterAPI", TweenMain.CreateRetweetUnofficial(statusText, false));
        }

        [Fact]
        public void CreateRetweetUnofficial_HashtagTest()
        {
            var statusText = """<a class="hashtag" href="https://twitter.com/search?q=%23OpenTween">#OpenTween</a>""";

            Assert.Equal("#OpenTween", TweenMain.CreateRetweetUnofficial(statusText, false));
        }

        [Fact]
        public void CreateRetweetUnofficial_SingleLineTest()
        {
            var statusText = "123<br>456<br>789";

            Assert.Equal("123 456 789", TweenMain.CreateRetweetUnofficial(statusText, false));
        }

        [Fact]
        public void CreateRetweetUnofficial_MultiLineTest()
        {
            var statusText = "123<br>456<br>789";

            Assert.Equal("123" + Environment.NewLine + "456" + Environment.NewLine + "789", TweenMain.CreateRetweetUnofficial(statusText, true));
        }

        [Fact]
        public void CreateRetweetUnofficial_DecodeTest()
        {
            var statusText = "&lt;&gt;&quot;&#39;&nbsp;";

            Assert.Equal("<>\"' ", TweenMain.CreateRetweetUnofficial(statusText, false));
        }

        [Fact]
        public void CreateRetweetUnofficial_WithFormatterTest()
        {
            // TweetFormatterでHTMLに整形 → CreateRetweetUnofficialで復元 までの動作が正しく行えているか

            var text = "#てすと @TwitterAPI \n http://t.co/KYi7vMZzRt";
            var entities = new TwitterEntity[]
            {
                new TwitterEntityHashtag
                {
                    Indices = new[] { 0, 4 },
                    Text = "てすと",
                },
                new TwitterEntityMention
                {
                    Indices = new[] { 5, 16 },
                    IdStr = "6253282",
                    Name = "Twitter API",
                    ScreenName = "twitterapi",
                },
                new TwitterEntityUrl
                {
                    Indices = new[] { 19, 41 },
                    DisplayUrl = "twitter.com",
                    ExpandedUrl = "http://twitter.com/",
                    Url = "http://t.co/KYi7vMZzRt",
                },
            };

            var html = TweetFormatter.AutoLinkHtml(text, entities);

            var expected = "#てすと @TwitterAPI " + Environment.NewLine + " http://twitter.com/";
            Assert.Equal(expected, TweenMain.CreateRetweetUnofficial(html, true));
        }

        [Theory]
        [InlineData("", true)]
        [InlineData("hoge", false)]
        [InlineData("@twitterapi ", true)]
        [InlineData("@twitterapi @opentween ", true)]
        [InlineData("@twitterapi @opentween hoge", false)]
        public void TextContainsOnlyMentions_Test(string input, bool expected)
            => Assert.Equal(expected, TweenMain.TextContainsOnlyMentions(input));
    }
}
