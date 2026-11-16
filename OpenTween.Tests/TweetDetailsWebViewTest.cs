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
using OpenTween.SocialProtocol.Twitter;
using Xunit;

namespace OpenTween
{
    public class TweetDetailsWebViewTest
    {
        [Theory]
        [InlineData("https://twitter.com/twitterapi", "https://twitter.com/twitterapi")]
        [InlineData(" https://example.com/ ", "https://example.com/")]
        [InlineData("//opentween/status/12345", "about://opentween/status/12345")]
        public void ResolveHref_Test(string href, string expected)
            => Assert.Equal(expected, TweetDetailsWebView.ResolveHref(href));

        [Fact]
        public void ResolveHref_InternalUriTest()
        {
            // TweenMain.OpenUriAsync は Authority が opentween の URL を内部リンクとして扱う
            var uri = new Uri(TweetDetailsWebView.ResolveHref("//opentween/status/12345"));
            Assert.Equal("opentween", uri.Authority);
            Assert.Equal("/status/12345", uri.AbsolutePath);
        }

        [Fact]
        public void InsertContentSecurityPolicy_Test()
        {
            var html = "<html><head><style></style></head><body>hoge</body></html>";
            var expected = """<html><head><meta http-equiv="Content-Security-Policy" content="script-src 'none'; object-src 'none'"><style></style></head><body>hoge</body></html>""";

            Assert.Equal(expected, TweetDetailsWebView.InsertContentSecurityPolicy(html));
        }

        [Fact]
        public void InsertContentSecurityPolicy_NoHeadTest()
        {
            var html = "<p>hoge</p>";
            var expected = """<head><meta http-equiv="Content-Security-Policy" content="script-src 'none'; object-src 'none'"></head><p>hoge</p>""";

            Assert.Equal(expected, TweetDetailsWebView.InsertContentSecurityPolicy(html));
        }

        [Theory]
        [InlineData(3, "const wheelNotchPx = 100;")]
        [InlineData(1, "const wheelNotchPx = 33.333333333333336;")]
        [InlineData(-1, "const wheelNotchPx = 0;")] // 画面単位のスクロール
        public void CreateBridgeScript_Test(int wheelScrollLines, string expected)
        {
            var script = TweetDetailsWebView.CreateBridgeScript(wheelScrollLines);

            Assert.Contains(expected, script);
            Assert.DoesNotContain("__WHEEL_NOTCH_PX__", script);
        }

        [Fact]
        public void ParseLinks_Test()
        {
            var html = """<html><body><p>""" +
                """<a href="https://t.co/hoge" title="https://example.com/?a=1&amp;b=2">example.com/…</a> """ +
                """<a class="hashtag" href="https://twitter.com/search?q=%23OpenTween">#<b>OpenTween</b></a> """ +
                """<a>no href</a>""" +
                TweetDetailsView.FormatQuoteTweetHtml(new TwitterStatusId("12345"), "quote", isReply: false) +
                "</p></body></html>";

            var links = TweetDetailsWebView.ParseLinks(html);

            Assert.Equal(
                new[]
                {
                    new TweetDetailsLink("https://t.co/hoge", "https://example.com/?a=1&b=2", "example.com/…", ""),
                    new TweetDetailsLink("https://twitter.com/search?q=%23OpenTween", "", "#OpenTween", "hashtag"),
                    new TweetDetailsLink("about://opentween/status/12345", "", "quote", "quote-tweet-link"),
                },
                links
            );
        }

        [WinFormsFact]
        public void NavigateToHtml_BeforeInitializationTest()
        {
            using var webView = new TweetDetailsWebView();

            var statusChanged = false;
            webView.StatusTextChanged += (s, e) => statusChanged = true;

            // WebView2 の初期化前でも HTML とリンクの情報を保持する
            webView.NavigateToHtml("""<a href="https://example.com/">example</a>""");

            Assert.Equal("""<a href="https://example.com/">example</a>""", webView.DocumentHtml);
            Assert.Single(webView.GetLinks());
            Assert.Null(webView.SelectedText);
            Assert.Equal("", webView.StatusText);
            Assert.False(statusChanged);
        }
    }
}
