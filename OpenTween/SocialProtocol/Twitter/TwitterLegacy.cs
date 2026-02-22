// OpenTween - Client of Twitter
// Copyright (c) 2007-2011 kiri_feather (@kiri_feather) <kiri.feather@gmail.com>
//           (c) 2008-2011 Moz (@syo68k)
//           (c) 2008-2011 takeshik (@takeshik) <http://www.takeshik.org/>
//           (c) 2010-2011 anis774 (@anis774) <http://d.hatena.ne.jp/anis774/>
//           (c) 2010-2011 fantasticswallow (@f_swallow) <http://twitter.com/f_swallow>
//           (c) 2011      Egtra (@egtra) <http://dev.activebasic.com/egtra/>
//           (c) 2013      kim_upsilon (@kim_upsilon) <https://upsilo.net/~upsilon/>
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
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenTween.Api;
using OpenTween.Api.DataModel;
using OpenTween.Api.GraphQL;
using OpenTween.Connection;
using OpenTween.Models;
using OpenTween.Setting;

namespace OpenTween.SocialProtocol.Twitter
{
    public class TwitterLegacy : IDisposable
    {
        #region Regexp from twitter-text-js

        // The code in this region code block incorporates works covered by
        // the following copyright and permission notices:
        //
        //   Copyright 2011 Twitter, Inc.
        //
        //   Licensed under the Apache License, Version 2.0 (the "License"); you
        //   may not use this work except in compliance with the License. You
        //   may obtain a copy of the License in the LICENSE file, or at:
        //
        //   http://www.apache.org/licenses/LICENSE-2.0
        //
        //   Unless required by applicable law or agreed to in writing, software
        //   distributed under the License is distributed on an "AS IS" BASIS,
        //   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
        //   implied. See the License for the specific language governing
        //   permissions and limitations under the License.

        // Hashtag用正規表現
        private const string LatinAccents = @"\u00c0-\u00d6\u00d8-\u00f6\u00f8-\u00ff\u0100-\u024f\u0253\u0254\u0256\u0257\u0259\u025b\u0263\u0268\u026f\u0272\u0289\u028b\u02bb\u1e00-\u1eff";
        private const string NonLatinHashtagChars = @"\u0400-\u04ff\u0500-\u0527\u1100-\u11ff\u3130-\u3185\uA960-\uA97F\uAC00-\uD7AF\uD7B0-\uD7FF";
        private const string CJHashtagCharacters = @"\u30A1-\u30FA\u30FC\u3005\uFF66-\uFF9F\uFF10-\uFF19\uFF21-\uFF3A\uFF41-\uFF5A\u3041-\u309A\u3400-\u4DBF\p{IsCJKUnifiedIdeographs}";
        private const string HashtagBoundary = @"^|$|\s|「|」|。|\.|!";
        private const string HashtagAlpha = $"[A-Za-z_{LatinAccents}{NonLatinHashtagChars}{CJHashtagCharacters}]";
        private const string HashtagAlphanumeric = $"[A-Za-z0-9_{LatinAccents}{NonLatinHashtagChars}{CJHashtagCharacters}]";
        private const string HashtagTerminator = $"[^A-Za-z0-9_{LatinAccents}{NonLatinHashtagChars}{CJHashtagCharacters}]";
        public const string Hashtag = $"({HashtagBoundary})(#|＃)({HashtagAlphanumeric}*{HashtagAlpha}{HashtagAlphanumeric}*)(?={HashtagTerminator}|{HashtagBoundary})";
        // URL正規表現
        private const string UrlValidPrecedingChars = @"(?:[^A-Za-z0-9@＠$#＃\ufffe\ufeff\uffff\u202a-\u202e]|^)";
        public const string UrlInvalidWithoutProtocolPrecedingChars = @"[-_./]$";
        private const string UrlInvalidDomainChars = @"\!'#%&'\(\)*\+,\\\-\.\/:;<=>\?@\[\]\^_{|}~\$\u2000-\u200a\u0009-\u000d\u0020\u0085\u00a0\u1680\u180e\u2028\u2029\u202f\u205f\u3000\ufffe\ufeff\uffff\u202a-\u202e";
        private const string UrlValidDomainChars = $@"[^{UrlInvalidDomainChars}]";
        private const string UrlValidSubdomain = $@"(?:(?:{UrlValidDomainChars}(?:[_-]|{UrlValidDomainChars})*)?{UrlValidDomainChars}\.)";
        private const string UrlValidDomainName = $@"(?:(?:{UrlValidDomainChars}(?:-|{UrlValidDomainChars})*)?{UrlValidDomainChars}\.)";
        private const string UrlValidGTLD = @"(?:(?:aero|asia|biz|cat|com|coop|edu|gov|info|int|jobs|mil|mobi|museum|name|net|org|pro|tel|travel|xxx)(?=[^0-9a-zA-Z]|$))";
        private const string UrlValidCCTLD = @"(?:(?:ac|ad|ae|af|ag|ai|al|am|an|ao|aq|ar|as|at|au|aw|ax|az|ba|bb|bd|be|bf|bg|bh|bi|bj|bm|bn|bo|br|bs|bt|bv|bw|by|bz|ca|cc|cd|cf|cg|ch|ci|ck|cl|cm|cn|co|cr|cs|cu|cv|cx|cy|cz|dd|de|dj|dk|dm|do|dz|ec|ee|eg|eh|er|es|et|eu|fi|fj|fk|fm|fo|fr|ga|gb|gd|ge|gf|gg|gh|gi|gl|gm|gn|gp|gq|gr|gs|gt|gu|gw|gy|hk|hm|hn|hr|ht|hu|id|ie|il|im|in|io|iq|ir|is|it|je|jm|jo|jp|ke|kg|kh|ki|km|kn|kp|kr|kw|ky|kz|la|lb|lc|li|lk|lr|ls|lt|lu|lv|ly|ma|mc|md|me|mg|mh|mk|ml|mm|mn|mo|mp|mq|mr|ms|mt|mu|mv|mw|mx|my|mz|na|nc|ne|nf|ng|ni|nl|no|np|nr|nu|nz|om|pa|pe|pf|pg|ph|pk|pl|pm|pn|pr|ps|pt|pw|py|qa|re|ro|rs|ru|rw|sa|sb|sc|sd|se|sg|sh|si|sj|sk|sl|sm|sn|so|sr|ss|st|su|sv|sy|sz|tc|td|tf|tg|th|tj|tk|tl|tm|tn|to|tp|tr|tt|tv|tw|tz|ua|ug|uk|us|uy|uz|va|vc|ve|vg|vi|vn|vu|wf|ws|ye|yt|za|zm|zw)(?=[^0-9a-zA-Z]|$))";
        private const string UrlValidPunycode = @"(?:xn--[0-9a-z]+)";
        private const string UrlValidDomain = $@"(?<domain>{UrlValidSubdomain}*{UrlValidDomainName}(?:{UrlValidGTLD}|{UrlValidCCTLD})|{UrlValidPunycode})";
        public const string UrlValidAsciiDomain = $@"(?:(?:[a-z0-9{LatinAccents}]+)\.)+(?:{UrlValidGTLD}|{UrlValidCCTLD}|{UrlValidPunycode})";
        public const string UrlInvalidShortDomain = $"^{UrlValidDomainName}{UrlValidCCTLD}$";
        private const string UrlValidPortNumber = @"[0-9]+";

        private const string UrlValidGeneralPathChars = $@"[a-z0-9!*';:=+,.$/%#\[\]\-_~|&{LatinAccents}]";
        private const string UrlBalanceParens = $@"(?:\({UrlValidGeneralPathChars}+\))";
        private const string UrlValidPathEndingChars = $@"(?:[+\-a-z0-9=_#/{LatinAccents}]|{UrlBalanceParens})";
        private const string Pth = "(?:" +
            "(?:" +
                $"{UrlValidGeneralPathChars}*" +
                $"(?:{UrlBalanceParens}{UrlValidGeneralPathChars}*)*" +
                UrlValidPathEndingChars +
                $")|(?:@{UrlValidGeneralPathChars}+/)" +
            ")";

        private const string Qry = @"(?<query>\?[a-z0-9!?*'();:&=+$/%#\[\]\-_.,~|]*[a-z0-9_&=#/])?";
        public const string RgUrl = $@"(?<before>{UrlValidPrecedingChars})" +
                                    "(?<url>(?<protocol>https?://)?" +
                                    $"(?<domain>{UrlValidDomain})" +
                                    $"(?::{UrlValidPortNumber})?" +
                                    $"(?<path>/{Pth}*)?" +
                                    Qry +
                                    ")";

        #endregion

        /// <summary>
        /// Twitter API のステータスページのURL
        /// </summary>
        public const string ServiceAvailabilityStatusUrl = "https://api.twitterstat.us/";

        /// <summary>
        /// ツイートへのパーマリンクURLを判定する正規表現
        /// </summary>
        public static readonly Regex StatusUrlRegex = new(@"https?://([^.]+\.)?(twitter|x)\.com/(#!/)?(?<ScreenName>[a-zA-Z0-9_]+)/status(es)?/(?<StatusId>[0-9]+)(/photo)?", RegexOptions.IgnoreCase);

        /// <summary>
        /// attachment_url に指定可能な URL を判定する正規表現
        /// </summary>
        public static readonly Regex AttachmentUrlRegex = new(
            @"https?://(
   (twitter|x)\.com/[0-9A-Za-z_]+/status/[0-9]+
 | mobile\.(twitter|x)\.com/[0-9A-Za-z_]+/status/[0-9]+
 | twitter\.com/messages/compose\?recipient_id=[0-9]+(&.+)?
)$",
            RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);

        /// <summary>
        /// FavstarやaclogなどTwitter関連サービスのパーマリンクURLからステータスIDを抽出する正規表現
        /// </summary>
        public static readonly Regex ThirdPartyStatusUrlRegex = new(
            @"https?://(?:[^.]+\.)?(?:
  favstar\.fm/users/[a-zA-Z0-9_]+/status/       # Favstar
| favstar\.fm/t/                                # Favstar (short)
| aclog\.koba789\.com/i/                        # aclog
| frtrt\.net/solo_status\.php\?status=          # RtRT
)(?<StatusId>[0-9]+)",
            RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);

        /// <summary>
        /// DM送信かどうかを判定する正規表現
        /// </summary>
        public static readonly Regex DMSendTextRegex = new(@"^DM? +(?<id>[a-zA-Z0-9_]+) +(?<body>.*)", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        public TwitterApi Api { get; }

        public TwitterAccountState AccountState { get; private set; } = new();

        public TwitterConfiguration Configuration
            => this.AccountState.Configuration;

        public TwitterTextConfiguration TextConfiguration
            => this.AccountState.TextConfiguration;

        public bool GetFollowersSuccess { get; private set; } = false;

        public bool GetNoRetweetSuccess { get; private set; } = false;

        private delegate void GetIconImageDelegate(PostClass post);

        private readonly object lockObj = new();

        private readonly TwitterPostFactory postFactory;
        private readonly PostUrlExpander urlExpander;

        private string? previousStatusId = null;

        public TwitterLegacy(TwitterApi api)
        {
            this.postFactory = new(TabInformations.GetInstance(), SettingManager.Instance.Common);
            this.urlExpander = new(ShortUrl.Instance);

            this.Api = api;
        }

        protected void ResetApiStatus()
            => this.AccountState.RateLimits.Clear();

        public void ClearAuthInfo()
        {
            this.AccountState.HasUnrecoverableError = true;
            this.ResetApiStatus();
        }

        public void Initialize(TwitterApiConnection apiConnection, TwitterAccountState accountState)
        {
            this.AccountState = accountState;

            this.ResetApiStatus();
            this.Api.Initialize(apiConnection);
        }

        public async Task<PostClass?> PostStatus(CreateTweetParams param)
        {
            this.CheckAccountState();

            if (DMSendTextRegex.IsMatch(param.Text))
            {
                var mediaId = param.MediaIds?.FirstOrDefault();

                await this.SendDirectMessage(param.Text, mediaId)
                    .ConfigureAwait(false);
                return null;
            }

            TwitterStatus status;

            if (this.Api.AuthType == APIAuthType.TwitterComCookie)
            {
                var request = new CreateTweetRequest
                {
                    TweetText = param.Text,
                    InReplyToTweetId = param.InReplyTo?.StatusId.ToTwitterStatusId(),
                    ExcludeReplyUserIds = param.ExcludeReplyUserIds.OfType<TwitterUserId>().ToArray(),
                    MediaIds = param.MediaIds.ToArray(),
                    AttachmentUrl = param.AttachmentUrl,
                };

                status = await request.Send(this.Api.Connection)
                    .ConfigureAwait(false);
            }
            else
            {
                using var response = await this.Api.StatusesUpdate(
                        param.Text,
                        param.InReplyTo?.StatusId.ToTwitterStatusId(),
                        param.MediaIds,
                        param.AutoPopulateReplyMetadata,
                        param.ExcludeReplyUserIds.OfType<TwitterUserId>().ToArray(),
                        param.AttachmentUrl
                    )
                    .ConfigureAwait(false);

                status = await response.LoadJsonAsync()
                    .ConfigureAwait(false);
            }

            this.AccountState.UpdateFromUser(status.User);

            if (status.IdStr == this.previousStatusId)
                throw new WebApiException("OK:Delaying?");

            this.previousStatusId = status.IdStr;

            // 投稿したものを返す
            var post = this.CreatePostsFromStatusData(status, firstLoad: false);

            return post;
        }

        public async Task<TwitterMediaId> UploadMedia(IMediaItem item, string? mediaCategory = null)
        {
            this.CheckAccountState();

            var mediaType = item.Extension switch
            {
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                _ => "application/octet-stream",
            };

            using var initResponse = await this.Api.MediaUploadInit(item.Size, mediaType, mediaCategory)
                .ConfigureAwait(false);

            var initMedia = await initResponse.LoadJsonAsync()
                .ConfigureAwait(false);

            var mediaId = new TwitterMediaId(initMedia.MediaIdStr);

            await this.Api.MediaUploadAppend(mediaId, 0, item)
                .ConfigureAwait(false);

            using var response = await this.Api.MediaUploadFinalize(mediaId)
                .ConfigureAwait(false);

            var media = await response.LoadJsonAsync()
                .ConfigureAwait(false);

            while (media.ProcessingInfo is TwitterUploadMediaResult.MediaProcessingInfo processingInfo)
            {
                switch (processingInfo.State)
                {
                    case "pending":
                        break;
                    case "in_progress":
                        break;
                    case "succeeded":
                        goto succeeded;
                    case "failed":
                        throw new WebApiException($"Err:Upload failed ({processingInfo.Error?.Name})");
                    default:
                        throw new WebApiException($"Err:Invalid state ({processingInfo.State})");
                }

                await Task.Delay(TimeSpan.FromSeconds(processingInfo.CheckAfterSecs ?? 5))
                    .ConfigureAwait(false);

                media = await this.Api.MediaUploadStatus(mediaId)
                    .ConfigureAwait(false);
            }

        succeeded:
            return mediaId;
        }

        public async Task SendDirectMessage(string postStr, TwitterMediaId? mediaId = null)
        {
            this.CheckAccountState();

            var mc = DMSendTextRegex.Match(postStr);

            var body = mc.Groups["body"].Value;
            var recipientName = mc.Groups["id"].Value;

            var recipient = await this.GetUserInfo(recipientName)
                .ConfigureAwait(false);

            var recipientUserId = new TwitterUserId(recipient.IdStr);
            using var response = await this.Api.DirectMessagesEventsNew(recipientUserId, body, mediaId)
                .ConfigureAwait(false);

            var messageEventSingle = await response.LoadJsonAsync()
                .ConfigureAwait(false);

            var post = await this.CreateDirectMessagesEventFromJson(messageEventSingle, firstLoad: false)
                .ConfigureAwait(false);

            var dmTab = TabInformations.GetInstance().DirectMessageTab;
            dmTab.AddPostQueue(post);
        }

        public async Task<TwitterUser> GetUserInfo(string screenName)
        {
            if (this.Api.AuthType == APIAuthType.TwitterComCookie)
            {
                var request = new UserByScreenNameRequest
                {
                    ScreenName = screenName,
                };
                var response = await request.Send(this.Api.Connection)
                    .ConfigureAwait(false);

                return response.ToTwitterUser();
            }
            else
            {
                var user = await this.Api.UsersShow(screenName)
                    .ConfigureAwait(false);

                return user;
            }
        }

        public string Username
            => this.AccountState.UserName;

        public TwitterUserId UserId
            => this.AccountState.UserId;

        public bool RestrictFavCheck { get; set; }

        public int? FollowersCount
            => this.AccountState.FollowersCount;

        public int? FriendsCount
            => this.AccountState.FriendsCount;

        public int? StatusesCount
            => this.AccountState.StatusesCount;

        /// <summary>
        /// 渡された取得件数がWORKERTYPEに応じた取得可能範囲に収まっているか検証する
        /// </summary>
        public static bool VerifyApiResultCount(MyCommon.WORKERTYPE type, int count)
            => count >= 20 && count <= GetMaxApiResultCount(type);

        /// <summary>
        /// 渡された取得件数が更新時の取得可能範囲に収まっているか検証する
        /// </summary>
        public static bool VerifyMoreApiResultCount(int count)
            => count >= 20 && count <= 200;

        /// <summary>
        /// 渡された取得件数が起動時の取得可能範囲に収まっているか検証する
        /// </summary>
        public static bool VerifyFirstApiResultCount(int count)
            => count >= 20 && count <= 200;

        /// <summary>
        /// WORKERTYPEに応じた取得可能な最大件数を取得する
        /// </summary>
        public static int GetMaxApiResultCount(MyCommon.WORKERTYPE type)
        {
            // 参照: REST APIs - 各endpointのcountパラメータ
            // https://dev.twitter.com/rest/public
            return type switch
            {
                MyCommon.WORKERTYPE.Timeline => 100,
                MyCommon.WORKERTYPE.Reply => 200,
                MyCommon.WORKERTYPE.UserTimeline => 200,
                MyCommon.WORKERTYPE.Favorites => 200,
                MyCommon.WORKERTYPE.List => 200, // 不明
                MyCommon.WORKERTYPE.PublicSearch => 100,
                _ => throw new InvalidOperationException("Invalid type: " + type),
            };
        }

        /// <summary>
        /// WORKERTYPEに応じた取得件数を取得する
        /// </summary>
        public static int GetApiResultCount(MyCommon.WORKERTYPE type, bool more, bool firstLoad)
        {
            if (SettingManager.Instance.Common.UseAdditionalCount)
            {
                switch (type)
                {
                    case MyCommon.WORKERTYPE.Favorites:
                        if (SettingManager.Instance.Common.FavoritesCountApi != 0)
                            return SettingManager.Instance.Common.FavoritesCountApi;
                        break;
                    case MyCommon.WORKERTYPE.List:
                        if (SettingManager.Instance.Common.ListCountApi != 0)
                            return SettingManager.Instance.Common.ListCountApi;
                        break;
                    case MyCommon.WORKERTYPE.PublicSearch:
                        if (SettingManager.Instance.Common.SearchCountApi != 0)
                            return SettingManager.Instance.Common.SearchCountApi;
                        break;
                    case MyCommon.WORKERTYPE.UserTimeline:
                        if (SettingManager.Instance.Common.UserTimelineCountApi != 0)
                            return SettingManager.Instance.Common.UserTimelineCountApi;
                        break;
                }
                if (more && SettingManager.Instance.Common.MoreCountApi != 0)
                {
                    return Math.Min(SettingManager.Instance.Common.MoreCountApi, GetMaxApiResultCount(type));
                }
                if (firstLoad && SettingManager.Instance.Common.FirstCountApi != 0 && type != MyCommon.WORKERTYPE.Reply)
                {
                    return Math.Min(SettingManager.Instance.Common.FirstCountApi, GetMaxApiResultCount(type));
                }
            }

            // 上記に当てはまらない場合の共通処理
            var count = SettingManager.Instance.Common.CountApi;

            if (type == MyCommon.WORKERTYPE.Reply)
                count = SettingManager.Instance.Common.CountApiReply;

            return Math.Min(count, GetMaxApiResultCount(type));
        }

        public async Task GetUserTimelineApi(UserTimelineTabModel tab, bool more, bool firstLoad)
        {
            this.CheckAccountState();

            var count = GetApiResultCount(MyCommon.WORKERTYPE.UserTimeline, more, firstLoad);

            TwitterStatus[] statuses;
            if (this.Api.AuthType == APIAuthType.TwitterComCookie)
            {
                var userId = tab.UserId as TwitterUserId;
                if (userId == null)
                {
                    var user = await this.GetUserInfo(tab.ScreenName)
                        .ConfigureAwait(false);

                    userId = new TwitterUserId(user.IdStr);
                    tab.UserId = userId;
                }

                var cursor = more ? tab.CursorBottom : tab.CursorTop;
                var request = new UserTweetsAndRepliesRequest(userId)
                {
                    Count = count,
                    Cursor = cursor?.As<TwitterGraphqlCursor>(),
                };
                var response = await request.Send(this.Api.Connection)
                    .ConfigureAwait(false);

                statuses = response.ToTwitterStatuses()
                    .Where(x => x.User.IdStr == userId.Id) // リプライツリーに含まれる他ユーザーのツイートを除外
                    .ToArray();

                tab.CursorBottom = response.CursorBottom;

                if (!more)
                    tab.CursorTop = response.CursorTop;
            }
            else
            {
                var maxId = more ? tab.CursorBottom?.As<TwitterStatusId>() : null;

                statuses = await this.Api.StatusesUserTimeline(tab.ScreenName, count, maxId)
                    .ConfigureAwait(false);

                if (statuses.Length > 0)
                {
                    var min = statuses.Select(x => new TwitterStatusId(x.IdStr)).Min();
                    tab.CursorBottom = new QueryCursor<TwitterStatusId>(CursorType.Bottom, min);
                }
            }

            var posts = this.CreatePostsFromJson(statuses, firstLoad);

            foreach (var post in posts)
                tab.AddPostQueue(post);
        }

        private PostClass CreatePostsFromStatusData(TwitterStatus status, bool firstLoad)
            => this.CreatePostsFromStatusData(status, firstLoad, favTweet: false);

        internal PostClass CreatePostsFromStatusData(TwitterStatus status, bool firstLoad, bool favTweet)
        {
            var post = this.postFactory.CreateFromStatus(status, this.UserId, this.AccountState.FollowerIds, firstLoad, favTweet);
            _ = this.urlExpander.Expand(post);

            return post;
        }

        internal PostClass[] CreatePostsFromJson(TwitterStatus[] statuses, bool firstLoad)
            => this.CreatePostsFromJson(statuses, firstLoad, favTweet: false);

        internal PostClass[] CreatePostsFromJson(TwitterStatus[] statuses, bool firstLoad, bool favTweet)
        {
            var posts = statuses.Select(x => this.CreatePostsFromStatusData(x, firstLoad, favTweet)).ToArray();

            TwitterPostFactory.AdjustSortKeyForPromotedPost(posts);

            return posts;
        }

        public async Task GetDirectMessageEvents(DirectMessagesTabModel dmTab, bool backward, bool firstLoad)
        {
            this.CheckAccountState();

            var count = 50;

            TwitterMessageEventList eventList;
            if (backward)
            {
                eventList = await this.Api.DirectMessagesEventsList(count, dmTab.NextCursor)
                    .ConfigureAwait(false);
            }
            else
            {
                eventList = await this.Api.DirectMessagesEventsList(count)
                    .ConfigureAwait(false);
            }

            dmTab.NextCursor = eventList.NextCursor;

            var posts = await this.CreateDirectMessagesEventFromJson(eventList, firstLoad)
                .ConfigureAwait(false);

            foreach (var post in posts)
                dmTab.AddPostQueue(post);
        }

        private async Task<PostClass> CreateDirectMessagesEventFromJson(TwitterMessageEventSingle eventSingle, bool firstLoad)
        {
            var eventList = new TwitterMessageEventList
            {
                Apps = new Dictionary<string, TwitterMessageEventList.App>(),
                Events = new[] { eventSingle.Event },
            };

            var posts = await this.CreateDirectMessagesEventFromJson(eventList, firstLoad)
                .ConfigureAwait(false);

            return posts.Single();
        }

        private async Task<PostClass[]> CreateDirectMessagesEventFromJson(TwitterMessageEventList eventList, bool firstLoad)
        {
            var events = eventList.Events
                .Where(x => x.Type == "message_create")
                .ToArray();

            if (events.Length == 0)
                return Array.Empty<PostClass>();

            var userIds = Enumerable.Concat(
                events.Select(x => new TwitterUserId(x.MessageCreate.SenderId)),
                events.Select(x => new TwitterUserId(x.MessageCreate.Target.RecipientId))
            ).Distinct().ToArray();

            var users = (await this.Api.UsersLookup(userIds).ConfigureAwait(false))
                .ToDictionary(x => new TwitterUserId(x.IdStr));

            var apps = eventList.Apps ?? new Dictionary<string, TwitterMessageEventList.App>();

            return this.CreateDirectMessagesEventFromJson(events, users, apps, firstLoad);
        }

        private PostClass[] CreateDirectMessagesEventFromJson(
            IReadOnlyCollection<TwitterMessageEvent> events,
            IReadOnlyDictionary<TwitterUserId, TwitterUser> users,
            IReadOnlyDictionary<string, TwitterMessageEventList.App> apps,
            bool firstLoad)
        {
            var posts = new List<PostClass>(capacity: events.Count);

            foreach (var eventItem in events)
            {
                var post = this.postFactory.CreateFromDirectMessageEvent(eventItem, users, apps, this.UserId, firstLoad);

                _ = this.urlExpander.Expand(post);

                posts.Add(post);
            }

            return posts.ToArray();
        }

        /// <summary>
        /// フォロワーIDを更新します。
        /// API 取得に失敗した場合はキャッシュファイルから復元します。
        /// </summary>
        /// <exception cref="WebApiException"/>
        public async Task RefreshFollowerIds()
        {
            if (MyCommon.EndingFlag) return;

            try
            {
                var cursor = -1L;
                var newFollowerIds = Enumerable.Empty<PersonId>();
                do
                {
                    var ret = await this.Api.FollowersIds(cursor)
                        .ConfigureAwait(false);

                    if (ret.Ids == null)
                        throw new WebApiException("ret.ids == null");

                    newFollowerIds = newFollowerIds.Concat(ret.Ids.Select(x => new TwitterUserId(x)));
                    cursor = ret.NextCursor;
                }
                while (cursor != 0);

                this.AccountState.FollowerIds = newFollowerIds.ToHashSet();
                this.GetFollowersSuccess = true;

                // 成功時にキャッシュへ保存
                this.SaveFollowerIdsCache(this.AccountState.FollowerIds);
            }
            catch (WebApiException)
            {
                // API 失敗時はキャッシュから復元を試みる
                if (this.AccountState.FollowerIds.Count == 0)
                {
                    var cached = this.LoadFollowerIdsCache();
                    if (cached.Count > 0)
                    {
                        this.AccountState.FollowerIds = cached;
                        this.GetFollowersSuccess = true;
                    }
                }

                throw;
            }
        }

        private string GetFollowerIdsCachePath()
        {
            var settingsPath = SettingManager.Instance.SettingsPath;
            return System.IO.Path.Combine(settingsPath, $"FollowerIds_{this.UserId}.txt");
        }

        private void SaveFollowerIdsCache(ISet<PersonId> followerIds)
        {
            try
            {
                var path = this.GetFollowerIdsCachePath();
                var lines = followerIds.Select(x => x.Id);
                System.IO.File.WriteAllLines(path, lines);
            }
            catch (Exception)
            {
                // キャッシュ保存失敗は無視
            }
        }

        private HashSet<PersonId> LoadFollowerIdsCache()
        {
            try
            {
                var path = this.GetFollowerIdsCachePath();
                if (!System.IO.File.Exists(path))
                    return new HashSet<PersonId>();

                var lines = System.IO.File.ReadAllLines(path);
                return lines
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => (PersonId)new TwitterUserId(x))
                    .ToHashSet();
            }
            catch (Exception)
            {
                return new HashSet<PersonId>();
            }
        }

        /// <summary>
        /// RT 非表示ユーザーを更新します
        /// </summary>
        /// <exception cref="WebApiException"/>
        public async Task RefreshNoRetweetIds()
        {
            if (MyCommon.EndingFlag) return;

            var noRetweetUserIds = await this.Api.NoRetweetIds()
                .ConfigureAwait(false);

            this.AccountState.NoRetweetUserIds = new HashSet<TwitterUserId>(noRetweetUserIds);
            this.GetNoRetweetSuccess = true;
        }

        public async Task GetListsApi()
        {
            this.CheckAccountState();

            var ownedLists = await TwitterLists.GetAllItemsAsync(x =>
                this.Api.ListsOwnerships(this.Username, cursor: x, count: 1000))
                    .ConfigureAwait(false);

            var subscribedLists = await TwitterLists.GetAllItemsAsync(x =>
                this.Api.ListsSubscriptions(this.Username, cursor: x, count: 1000))
                    .ConfigureAwait(false);

            TabInformations.GetInstance().SubscribableLists = Enumerable.Concat(ownedLists, subscribedLists)
                .Select(x => new ListElement(x, this))
                .ToList();
        }

        public async Task DeleteList(long listId)
        {
            await this.Api.ListsDestroy(listId)
                .IgnoreResponse()
                .ConfigureAwait(false);

            var tabinfo = TabInformations.GetInstance();

            tabinfo.SubscribableLists = tabinfo.SubscribableLists
                .Where(x => x.Id != listId)
                .ToList();
        }

        public async Task<ListElement> EditList(long listId, string new_name, bool isPrivate, string description)
        {
            using var response = await this.Api.ListsUpdate(listId, new_name, description, isPrivate)
                .ConfigureAwait(false);

            var list = await response.LoadJsonAsync()
                .ConfigureAwait(false);

            return new ListElement(list, this);
        }

        public async Task<long> GetListMembers(long listId, List<UserInfo> lists, long cursor)
        {
            this.CheckAccountState();

            var users = await this.Api.ListsMembers(listId, cursor)
                .ConfigureAwait(false);

            Array.ForEach(users.Users, u => lists.Add(new UserInfo(u)));

            return users.NextCursor;
        }

        public async Task CreateListApi(string listName, bool isPrivate, string description)
        {
            this.CheckAccountState();

            using var response = await this.Api.ListsCreate(listName, description, isPrivate)
                .ConfigureAwait(false);

            var list = await response.LoadJsonAsync()
                .ConfigureAwait(false);

            TabInformations.GetInstance().SubscribableLists.Add(new ListElement(list, this));
        }

        public async Task<bool> ContainsUserAtList(long listId, string user)
        {
            this.CheckAccountState();

            try
            {
                await this.Api.ListsMembersShow(listId, user)
                    .ConfigureAwait(false);

                return true;
            }
            catch (TwitterApiException ex)
                when (ex.Errors.Any(x => x.Code == TwitterErrorCode.NotFound))
            {
                return false;
            }
        }

        public async Task<TwitterRateLimitCollection?> GetInfoApi()
        {
            if (this.AccountState.HasUnrecoverableError)
                return null;

            if (MyCommon.EndingFlag) return null;

            var limits = await this.Api.ApplicationRateLimitStatus()
                .ConfigureAwait(false);

            this.AccountState.RateLimits.UpdateFromJson(limits);

            return this.AccountState.RateLimits;
        }

        /// <summary>
        /// ブロック中のユーザーを更新します
        /// </summary>
        /// <exception cref="WebApiException"/>
        public async Task RefreshBlockIds()
        {
            if (MyCommon.EndingFlag) return;

            var cursor = -1L;
            var newBlockIds = Enumerable.Empty<PersonId>();
            do
            {
                var ret = await this.Api.BlocksIds(cursor)
                    .ConfigureAwait(false);

                newBlockIds = newBlockIds.Concat(ret.Ids.Select(x => new TwitterUserId(x)));
                cursor = ret.NextCursor;
            }
            while (cursor != 0);

            var blockIdsSet = newBlockIds.ToHashSet();
            blockIdsSet.Remove(this.UserId); // 元のソースにあったので一応残しておく

            this.AccountState.BlockedUserIds = blockIdsSet;
        }

        /// <summary>
        /// ミュート中のユーザーIDを更新します
        /// </summary>
        /// <exception cref="WebApiException"/>
        public async Task RefreshMuteUserIdsAsync()
        {
            if (MyCommon.EndingFlag) return;

            var ids = await TwitterIds.GetAllItemsAsync(x => this.Api.MutesUsersIds(x))
                .ConfigureAwait(false);

            this.AccountState.MutedUserIds = ids.ToHashSet<PersonId>();
        }

        public string[] GetHashList()
            => this.postFactory.GetReceivedHashtags();

        internal void CheckAccountState()
        {
            if (this.AccountState.HasUnrecoverableError)
                throw new WebApiException("Auth error. Check your account");
        }

        public int GetTextLengthRemain(string postText)
        {
            var matchDm = DMSendTextRegex.Match(postText);
            if (matchDm.Success)
                return this.GetTextLengthRemainDM(matchDm.Groups["body"].Value);

            return this.GetTextLengthRemainWeighted(postText);
        }

        private int GetTextLengthRemainDM(string postText)
        {
            var textLength = 0;

            var pos = 0;
            while (pos < postText.Length)
            {
                textLength++;

                if (char.IsSurrogatePair(postText, pos))
                    pos += 2; // サロゲートペアの場合は2文字分進める
                else
                    pos++;
            }

            var urls = TweetExtractor.ExtractUrls(postText);
            foreach (var url in urls)
            {
                var shortUrlLength = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    ? this.Configuration.ShortUrlLengthHttps
                    : this.Configuration.ShortUrlLength;

                textLength += shortUrlLength - url.Length;
            }

            return this.Configuration.DmTextCharacterLimit - textLength;
        }

        private int GetTextLengthRemainWeighted(string postText)
        {
            var config = this.TextConfiguration;
            var totalWeight = 0;

            int GetWeightFromCodepoint(int codepoint)
            {
                foreach (var weightRange in config.Ranges)
                {
                    if (codepoint >= weightRange.Start && codepoint <= weightRange.End)
                        return weightRange.Weight;
                }

                return config.DefaultWeight;
            }

            var urls = TweetExtractor.ExtractUrlEntities(postText).ToArray();
            var emojis = config.EmojiParsingEnabled
                ? TweetExtractor.ExtractEmojiEntities(postText).ToArray()
                : Array.Empty<TwitterEntityEmoji>();

            var codepoints = postText.ToCodepoints().ToArray();
            var index = 0;
            while (index < codepoints.Length)
            {
                var urlEntity = urls.FirstOrDefault(x => x.Indices[0] == index);
                if (urlEntity != null)
                {
                    totalWeight += config.TransformedURLLength * config.Scale;
                    index = urlEntity.Indices[1];
                    continue;
                }

                var emojiEntity = emojis.FirstOrDefault(x => x.Indices[0] == index);
                if (emojiEntity != null)
                {
                    totalWeight += GetWeightFromCodepoint(codepoints[index]);
                    index = emojiEntity.Indices[1];
                    continue;
                }

                var codepoint = codepoints[index];
                totalWeight += GetWeightFromCodepoint(codepoint);

                index++;
            }

            var remainWeight = config.MaxWeightedTweetLength * config.Scale - totalWeight;

            return remainWeight / config.Scale;
        }

        public bool IsDisposed { get; private set; } = false;

        protected virtual void Dispose(bool disposing)
        {
            if (this.IsDisposed)
                return;

            if (disposing)
            {
                this.Api.Dispose();
            }

            this.IsDisposed = true;
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
