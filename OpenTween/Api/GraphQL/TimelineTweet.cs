// OpenTween - Client of Twitter
// Copyright (c) 2023 kim_upsilon (@kim_upsilon) <https://upsilo.net/~upsilon/>
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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;
using OpenTween.Api.DataModel;

namespace OpenTween.Api.GraphQL
{
    public class TimelineTweet
    {
        public const string TypeName = nameof(TimelineTweet);

        public XElement Element { get; }

        public bool IsAvailable
            => this.resultElm != null && !this.IsTombstoneResult(this.resultElm) && this.HasLegacyProperty(this.resultElm);

        private readonly XElement? resultElm;

        public TimelineTweet(XElement element)
        {
            var typeName = element.Element("itemType")?.Value;
            if (typeName != TypeName)
                throw new ArgumentException($"Invalid itemType: {typeName}", nameof(element));

            this.Element = element;
            this.resultElm = this.TryGetResultElm();
        }

        private XElement? TryGetResultElm()
            => this.Element.XPathSelectElement("tweet_results/result");

        private bool IsTombstoneResult([NotNullWhen(true)]XElement? resultElm)
            => resultElm?.Element("__typename")?.Value == "TweetTombstone";

        private bool HasLegacyProperty(XElement? resultElm)
            => resultElm?.XPathSelectElement("legacy|tweet/legacy") != null;

        public TwitterStatus ToTwitterStatus()
        {
            this.ThrowIfTweetIsNotAvailable();

            try
            {
                var resultElm = this.resultElm ?? throw CreateParseError();
                var status = TimelineTweet.ParseTweetUnion(resultElm);

                if (this.Element.Element("promotedMetadata") != null)
                    status.IsPromoted = true;

                return status;
            }
            catch (WebApiException ex)
            {
                ex.ResponseText = JsonUtils.JsonXmlToString(this.Element);
                MyCommon.TraceOut(ex);
                throw;
            }
        }

        public void ThrowIfTweetIsNotAvailable()
        {
            if (this.IsAvailable)
                return;

            string? tombstoneText = null;
            if (this.IsTombstoneResult(this.resultElm))
                tombstoneText = this.resultElm.XPathSelectElement("tombstone/text/text")?.Value;

            var message = tombstoneText ?? "Tweet is not available";
            var json = JsonUtils.JsonXmlToString(this.Element);

            throw new WebApiException(message, json);
        }

        public static TwitterStatus ParseTweetUnion(XElement tweetUnionElm)
        {
            var tweetElm = GetTweetTypeName(tweetUnionElm) switch
            {
                "Tweet" => tweetUnionElm,
                "TweetWithVisibilityResults" => tweetUnionElm.Element("tweet") ?? throw CreateParseError(),
                _ => throw CreateParseError(),
            };

            return TimelineTweet.ParseTweet(tweetElm);
        }

        public static string GetTweetTypeName(XElement tweetUnionElm)
            => tweetUnionElm.Element("__typename")?.Value ?? throw CreateParseError();

        public static TwitterStatus ParseTweet(XElement tweetElm)
        {
            var tweetLegacyElm = tweetElm.Element("legacy") ?? throw CreateParseError();
            var userElm = tweetElm.Element("core")?.Element("user_results")?.Element("result") ?? throw CreateParseError();
            var retweetedTweetElm = tweetLegacyElm.Element("retweeted_status_result")?.Element("result");
            var user = new TwitterGraphqlUser(userElm);
            var quotedTweetElm = tweetElm.Element("quoted_status_result")?.Element("result") ?? null;
            var quotedStatusPermalink = tweetLegacyElm.Element("quoted_status_permalink") ?? null;

            // 引用元が削除・凍結・非公開などで参照できない場合、__typename が Tweet 以外
            // （TweetTombstone や TweetUnavailable 等）になる。その場合は引用ツイートを展開しない
            var isQuotedTweetAvailable = quotedTweetElm != null
                && GetTweetTypeName(quotedTweetElm) is "Tweet" or "TweetWithVisibilityResults";

            // note_tweet: 長文ツイート（280文字超）のフルテキストとエンティティ
            var noteTweetElm = tweetElm.XPathSelectElement("note_tweet/note_tweet_results/result");

            static string GetText(XElement elm, string name)
                => elm.Element(name)?.Value ?? throw CreateParseError();

            static string? GetTextOrNull(XElement elm, string name)
                => elm.Element(name)?.Value;

            // note_tweet が存在する場合はそちらのテキストとエンティティを優先して使用
            var fullText = noteTweetElm?.Element("text")?.Value
                ?? GetText(tweetLegacyElm, "full_text");

            var entitiesElm = noteTweetElm?.Element("entity_set") ?? tweetLegacyElm.Element("entities");

            return new()
            {
                IdStr = GetText(tweetElm, "rest_id"),
                Source = GetText(tweetElm, "source"),
                CreatedAt = GetText(tweetLegacyElm, "created_at"),
                FullText = fullText,
                InReplyToScreenName = GetTextOrNull(tweetLegacyElm, "in_reply_to_screen_name"),
                InReplyToStatusIdStr = GetTextOrNull(tweetLegacyElm, "in_reply_to_status_id_str"),
                InReplyToUserIdStr = GetTextOrNull(tweetLegacyElm, "in_reply_to_user_id_str"),
                Favorited = GetTextOrNull(tweetLegacyElm, "favorited") is string favorited ? favorited == "true" : null,
                Entities = new()
                {
                    UserMentions = (entitiesElm ?? tweetLegacyElm.Element("entities"))?.XPathSelectElements("user_mentions/item")
                        .Select(x => new TwitterEntityMention()
                        {
                            Indices = x.XPathSelectElements("indices/item").Select(x => int.Parse(x.Value)).ToArray(),
                            IdStr = GetText(x, "id_str"),
                            ScreenName = GetText(x, "screen_name"),
                        })
                        .ToArray() ?? Array.Empty<TwitterEntityMention>(),
                    Urls = (entitiesElm ?? tweetLegacyElm.Element("entities"))?.XPathSelectElements("urls/item")
                        .Select(x => new TwitterEntityUrl()
                        {
                            Indices = x.XPathSelectElements("indices/item").Select(x => int.Parse(x.Value)).ToArray(),
                            DisplayUrl = GetTextOrNull(x, "display_url"),
                            ExpandedUrl = GetTextOrNull(x, "expanded_url"),
                            Url = GetText(x, "url"),
                        })
                        .ToArray() ?? Array.Empty<TwitterEntityUrl>(),
                    Hashtags = (entitiesElm ?? tweetLegacyElm.Element("entities"))?.XPathSelectElements("hashtags/item")
                        .Select(x => new TwitterEntityHashtag()
                        {
                            Indices = x.XPathSelectElements("indices/item").Select(x => int.Parse(x.Value)).ToArray(),
                            Text = GetText(x, "text"),
                        })
                        .ToArray() ?? Array.Empty<TwitterEntityHashtag>(),
                },
                ExtendedEntities = new()
                {
                    Media = tweetLegacyElm.XPathSelectElements("extended_entities/media/item")
                        .Select(x => new TwitterEntityMedia()
                        {
                            Indices = x.XPathSelectElements("indices/item").Select(x => int.Parse(x.Value)).ToArray(),
                            DisplayUrl = GetText(x, "display_url"),
                            ExpandedUrl = GetText(x, "expanded_url"),
                            Url = GetText(x, "url"),
                            MediaUrlHttps = GetText(x, "media_url_https"),
                            Type = GetText(x, "type"),
                            AltText = GetTextOrNull(x, "ext_alt_text"),
                        })
                        .ToArray(),
                },
                User = user.ToTwitterUser(),
                RetweetedStatus = retweetedTweetElm != null ? TimelineTweet.ParseTweetUnion(retweetedTweetElm) : null,
                IsQuoteStatus = GetTextOrNull(tweetLegacyElm, "is_quote_status") == "true",
                QuotedStatus = isQuotedTweetAvailable ? TimelineTweet.ParseTweetUnion(quotedTweetElm!) : null,
                QuotedStatusIdStr = GetTextOrNull(tweetLegacyElm, "quoted_status_id_str"),
                QuotedStatusPermalink = quotedStatusPermalink == null ? null : new()
                {
                    Url = GetText(quotedStatusPermalink, "url"),
                    Expanded = GetText(quotedStatusPermalink, "expanded"),
                    Display = GetText(quotedStatusPermalink, "display"),
                },
            };
        }

        private static Exception CreateParseError()
            => throw new WebApiException("Parse error on TimelineTweet");

        public static TimelineTweet[] ExtractTimelineTweets(XElement element)
        {
            return element.XPathSelectElements($"//itemContent[itemType[text()='{TypeName}']][tweetDisplayType[text()='Tweet' or text()='SelfThread']]")
                .Select(x => new TimelineTweet(x))
                .ToArray();
        }
    }
}
