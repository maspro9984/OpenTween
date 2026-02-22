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

using System.Linq;
using System.Threading.Tasks;
using OpenTween.Api;
using OpenTween.Api.DataModel;
using OpenTween.Connection;
using OpenTween.Models;
using OpenTween.Setting;

namespace OpenTween.SocialProtocol.Twitter
{
    public class TwitterV1Client : ISocialProtocolClient
    {
        private readonly TwitterAccount account;

        public TwitterV1Client(TwitterAccount account)
        {
            this.account = account;
        }

        public async Task<UserInfo> VerifyCredentials()
        {
            var twitterUser = await this.account.Legacy.Api.AccountVerifyCredentials()
                .ConfigureAwait(false);

            this.account.AccountState.UpdateFromUser(twitterUser);

            return new(twitterUser);
        }

        public async Task<PostClass> GetPostById(PostId postId, bool firstLoad)
        {
            this.account.Legacy.CheckAccountState();

            var statusId = this.AssertTwitterStatusId(postId);
            var status = await this.account.Legacy.Api.StatusesShow(statusId)
                .ConfigureAwait(false);

            var post = this.account.Legacy.CreatePostsFromStatusData(status, firstLoad, favTweet: false);

            return post;
        }

        public async Task<TimelineResponse> GetHomeTimeline(int count, IQueryCursor? cursor, bool firstLoad)
        {
            this.account.Legacy.CheckAccountState();

            var (sinceId, maxId) = GetCursorParams(cursor);

            var statuses = await this.account.Legacy.Api.StatusesHomeTimeline(count, maxId, sinceId)
                .ConfigureAwait(false);

            var (cursorTop, cursorBottom) = GetCursorFromResponse(statuses);
            var posts = this.account.Legacy.CreatePostsFromJson(statuses, firstLoad);

            var filter = new TimelineResponseFilter(this.account.AccountState)
            {
                IsHomeTimeline = true,
            };
            posts = filter.Run(posts);

            return new(posts, cursorTop, cursorBottom);
        }

        public async Task<TimelineResponse> GetMentionsTimeline(int count, IQueryCursor? cursor, bool firstLoad)
        {
            this.account.Legacy.CheckAccountState();

            var (sinceId, maxId) = GetCursorParams(cursor);

            var statuses = await this.account.Legacy.Api.StatusesMentionsTimeline(count, maxId, sinceId)
                .ConfigureAwait(false);

            var (cursorTop, cursorBottom) = GetCursorFromResponse(statuses);
            var posts = this.account.Legacy.CreatePostsFromJson(statuses, firstLoad);

            var filter = new TimelineResponseFilter(this.account.AccountState);
            posts = filter.Run(posts);

            return new(posts, cursorTop, cursorBottom);
        }

        public async Task<TimelineResponse> GetFavoritesTimeline(int count, IQueryCursor? cursor, bool firstLoad)
        {
            this.account.Legacy.CheckAccountState();

            var (sinceId, maxId) = GetCursorParams(cursor);

            var statuses = await this.account.Legacy.Api.FavoritesList(count, maxId, sinceId)
                .ConfigureAwait(false);

            var (cursorTop, cursorBottom) = GetCursorFromResponse(statuses);
            var posts = this.account.Legacy.CreatePostsFromJson(statuses, firstLoad, favTweet: true);

            var filter = new TimelineResponseFilter(this.account.AccountState);
            posts = filter.Run(posts);

            return new(posts, cursorTop, cursorBottom);
        }

        public async Task<TimelineResponse> GetListTimeline(long listId, int count, IQueryCursor? cursor, bool firstLoad)
        {
            this.account.Legacy.CheckAccountState();
            var (sinceId, maxId) = GetCursorParams(cursor);

            var includeRTs = SettingManager.Instance.Common.IsListsIncludeRts;
            var statuses = await this.account.Legacy.Api.ListsStatuses(listId, count, maxId, sinceId, includeRTs)
                .ConfigureAwait(false);

            var (cursorTop, cursorBottom) = GetCursorFromResponse(statuses);
            var posts = this.account.Legacy.CreatePostsFromJson(statuses, firstLoad);

            var filter = new TimelineResponseFilter(this.account.AccountState);
            posts = filter.Run(posts);

            return new(posts, cursorTop, cursorBottom);
        }

        public async Task<TimelineResponse> GetSearchTimeline(string query, string lang, int count, IQueryCursor? cursor, bool firstLoad)
        {
            this.account.Legacy.CheckAccountState();

            var (sinceId, maxId) = GetCursorParams(cursor);

            var searchResult = await this.account.Legacy.Api.SearchTweets(query, lang, count, maxId, sinceId)
                .ConfigureAwait(false);

            var statuses = searchResult.Statuses;

            var (cursorTop, cursorBottom) = GetCursorFromResponse(statuses);
            var posts = this.account.Legacy.CreatePostsFromJson(statuses, firstLoad);

            var filter = new TimelineResponseFilter(this.account.AccountState);
            posts = filter.Run(posts);

            return new(posts, cursorTop, cursorBottom);
        }

        public async Task<PostClass[]> GetRelatedPosts(PostClass targetPost, bool firstLoad)
        {
            var tabinfo = TabInformations.GetInstance();
            var fetcher = new RelatedTweetsFetcher(tabinfo, this);

            return await fetcher.Run(targetPost, firstLoad);
        }

        public async Task<PostClass?> CreatePost(PostStatusParams postParams)
        {
            var formatter = new CreateTweetFormatter(this.account);
            var createTweetParams = formatter.CreateParams(postParams);

            return await this.account.Legacy.PostStatus(createTweetParams)
                .ConfigureAwait(false);
        }

        public int GetTextLengthRemain(PostStatusParams postParams)
        {
            var formatter = new CreateTweetFormatter(this.account);
            return formatter.GetTextLengthRemain(postParams);
        }

        public async Task DeletePost(PostId postId)
        {
            if (postId is TwitterDirectMessageId dmId)
            {
                await this.account.Legacy.Api.DirectMessagesEventsDestroy(dmId)
                    .ConfigureAwait(false);
                return;
            }

            var statusId = this.AssertTwitterStatusId(postId);

            await this.account.Legacy.Api.StatusesDestroy(statusId)
                .IgnoreResponse();
        }

        public async Task FavoritePost(PostId postId)
        {
            var statusId = this.AssertTwitterStatusId(postId);

            try
            {
                await this.account.Legacy.Api.FavoritesCreate(statusId)
                    .IgnoreResponse()
                    .ConfigureAwait(false);
            }
            catch (TwitterApiException ex)
                when (ex.Errors.All(x => x.Code == TwitterErrorCode.AlreadyFavorited))
            {
                // エラーコード 139 のみの場合は成功と見なす
            }
        }

        public async Task UnfavoritePost(PostId postId)
        {
            var statusId = this.AssertTwitterStatusId(postId);

            await this.account.Legacy.Api.FavoritesDestroy(statusId)
                .IgnoreResponse()
                .ConfigureAwait(false);
        }

        public async Task<PostClass?> RetweetPost(PostId postId)
        {
            var statusId = this.AssertTwitterStatusId(postId);

            using var response = await this.account.Legacy.Api.StatusesRetweet(statusId)
                .ConfigureAwait(false);

            var status = await response.LoadJsonAsync()
                .ConfigureAwait(false);

            // Retweet判定
            if (status.RetweetedStatus == null)
                throw new WebApiException("Invalid Json!");

            // Retweetしたものを返す
            return this.CreatePostFromJson(status);
        }

        public async Task UnretweetPost(PostId postId)
        {
            var statusId = this.AssertTwitterStatusId(postId);

            await this.account.Legacy.Api.StatusesUnretweet(statusId)
                .IgnoreResponse()
                .ConfigureAwait(false);
        }

        public async Task RefreshConfiguration()
        {
            // 各タスクを独立して実行し、一部が失敗してもフォロワーID等の取得を継続する
            WebApiException? firstError = null;

            async Task RunSafe(Task task)
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (WebApiException ex)
                {
                    firstError ??= ex;
                }
            }

            await Task.WhenAll(
                RunSafe(this.account.Legacy.RefreshFollowerIds()),
                RunSafe(this.account.Legacy.RefreshBlockIds()),
                RunSafe(this.account.Legacy.RefreshMuteUserIdsAsync()),
                RunSafe(this.account.Legacy.RefreshNoRetweetIds())
            );

            if (firstError != null)
                throw firstError;
        }

        private TwitterStatusId AssertTwitterStatusId(PostId postId)
        {
            return postId is TwitterStatusId statusId
                ? statusId
                : throw new WebApiException($"Not supported type: {postId.GetType()}");
        }

        private PostClass CreatePostFromJson(TwitterStatus status)
            => this.account.Legacy.CreatePostsFromStatusData(status, firstLoad: false, favTweet: false);

        public static (TwitterStatusId? SinceId, TwitterStatusId? MaxId) GetCursorParams(IQueryCursor? cursor)
        {
            TwitterStatusId? sinceId = null, maxId = null;

            if (cursor is QueryCursor<TwitterStatusId> statusIdCursor)
            {
                if (statusIdCursor.Type == CursorType.Top)
                    sinceId = statusIdCursor.Value;
                else if (statusIdCursor.Type == CursorType.Bottom)
                    maxId = statusIdCursor.Value;
            }

            return (sinceId, maxId);
        }

        public static (IQueryCursor? CursorTop, IQueryCursor? CursorBottom) GetCursorFromResponse(TwitterStatus[] statuses)
        {
            IQueryCursor? cursorTop = null, cursorBottom = null;

            if (statuses.Length > 0)
            {
                var (min, max) = statuses.Select(x => new TwitterStatusId(x.IdStr)).MinMax();
                cursorTop = new QueryCursor<TwitterStatusId>(CursorType.Top, max);
                cursorBottom = new QueryCursor<TwitterStatusId>(CursorType.Bottom, min);
            }

            return (cursorTop, cursorBottom);
        }
    }
}
